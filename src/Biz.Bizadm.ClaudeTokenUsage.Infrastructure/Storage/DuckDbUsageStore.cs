using DuckDB.NET.Data;
using System.Data;
using System.Data.Common;
using Biz.Bizadm.ClaudeTokenUsage.Core;

namespace Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage
{
    /// <summary>
    /// DuckDB에 토큰·비용 사용량을 단위별 롤업으로 저장한다.
    /// <para>
    /// 단위마다 확정 경계(<c>rollup_state.finalized_until</c>)를 둔다. 경계 앞의 구간은 확정되어 다시 계산하지 않고,
    /// 늦게 온 값은 증분으로 더한다. 경계 뒤의 구간은 유지 작업마다 하위 단위에서 다시 계산한다.
    /// 받기·유지 작업·조회는 연결 하나와 잠금 하나로 순서대로 처리해 이중 반영을 막는다.
    /// </para>
    /// </summary>
    public sealed class DuckDbUsageStore : IDisposable
    {
        private const string TokenTable = "token_usage";
        private const string CostTable = "cost_usage";

        private readonly DuckDBConnection connection;
        private readonly TimeProvider timeProvider;
        private readonly SemaphoreSlim gate = new(1, 1);

        private DuckDbUsageStore(DuckDBConnection connection, TimeProvider timeProvider)
        {
            this.connection = connection;
            this.timeProvider = timeProvider;
        }

        /// <summary>
        /// DB 파일을 열고(없으면 만들고) 스키마를 준비한다. 파일의 폴더가 없으면 만든다.
        /// </summary>
        public static DuckDbUsageStore Open(string databaseFilePath, TimeProvider timeProvider)
        {
            string fullPath = Path.GetFullPath(databaseFilePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            DuckDBConnection connection = new($"Data Source={fullPath}");
            try
            {
                connection.Open();
                DuckDbUsageStore store = new(connection, timeProvider);
                store.EnsureSchema();
                return store;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private void EnsureSchema()
        {
            Execute(null, """
                CREATE TABLE IF NOT EXISTS token_usage (
                  unit VARCHAR NOT NULL,
                  bucket_start TIMESTAMP NOT NULL,
                  user_id VARCHAR NOT NULL,
                  model VARCHAR NOT NULL,
                  token_type VARCHAR NOT NULL,
                  tokens BIGINT NOT NULL,
                  PRIMARY KEY (unit, bucket_start, user_id, model, token_type)
                );
                CREATE TABLE IF NOT EXISTS cost_usage (
                  unit VARCHAR NOT NULL,
                  bucket_start TIMESTAMP NOT NULL,
                  user_id VARCHAR NOT NULL,
                  model VARCHAR NOT NULL,
                  cost_usd DOUBLE NOT NULL,
                  PRIMARY KEY (unit, bucket_start, user_id, model)
                );
                CREATE TABLE IF NOT EXISTS rollup_state (
                  unit VARCHAR PRIMARY KEY,
                  finalized_until TIMESTAMP NOT NULL
                );
                """);
        }

        /// <summary>
        /// 포인트를 저장한다. 포인트 시각(KST)마다 단위를 분→년 순으로 보며,
        /// 보관 기간 밖이면 건너뛰고, 분은 더하고, 상위 단위는 확정된 구간일 때만 증분으로 더한다
        /// (확정 전 구간은 다음 유지 작업이 하위 행에서 가져간다).
        /// </summary>
        public async Task IngestAsync(IReadOnlyCollection<TokenUsagePoint> tokens, IReadOnlyCollection<CostUsagePoint> costs, CancellationToken cancellationToken = default)
        {
            if (tokens.Count == 0 && costs.Count == 0)
                return;

            await gate.WaitAsync(cancellationToken);
            try
            {
                DateTime now = KstClock.Now(timeProvider);
                using DbTransaction transaction = connection.BeginTransaction();
                Dictionary<UsageUnit, DateTime> finalized = LoadFinalized(transaction);

                Dictionary<(UsageUnit Unit, DateTime Bucket, string User, string Model, string Type), long> tokenSums = [];
                foreach (TokenUsagePoint point in tokens)
                {
                    foreach ((UsageUnit unit, DateTime bucket) in TargetBuckets(point.Time, now, finalized))
                    {
                        var key = (unit, bucket, point.User, point.Model, point.TokenType);
                        tokenSums[key] = tokenSums.GetValueOrDefault(key) + point.Tokens;
                    }
                }

                Dictionary<(UsageUnit Unit, DateTime Bucket, string User, string Model), double> costSums = [];
                foreach (CostUsagePoint point in costs)
                {
                    foreach ((UsageUnit unit, DateTime bucket) in TargetBuckets(point.Time, now, finalized))
                    {
                        var key = (unit, bucket, point.User, point.Model);
                        costSums[key] = costSums.GetValueOrDefault(key) + point.CostUsd;
                    }
                }

                foreach (var ((unit, bucket, user, model, type), value) in tokenSums)
                {
                    Execute(transaction, """
                        INSERT INTO token_usage VALUES ($unit, $bucket, $user, $model, $type, $value)
                        ON CONFLICT (unit, bucket_start, user_id, model, token_type) DO UPDATE SET tokens = tokens + EXCLUDED.tokens
                        """,
                        ("unit", unit.ToName()), ("bucket", bucket), ("user", user), ("model", model), ("type", type), ("value", value));
                }

                foreach (var ((unit, bucket, user, model), value) in costSums)
                {
                    Execute(transaction, """
                        INSERT INTO cost_usage VALUES ($unit, $bucket, $user, $model, $value)
                        ON CONFLICT (unit, bucket_start, user_id, model) DO UPDATE SET cost_usd = cost_usd + EXCLUDED.cost_usd
                        """,
                        ("unit", unit.ToName()), ("bucket", bucket), ("user", user), ("model", model), ("value", value));
                }

                transaction.Commit();
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 한 포인트를 더할 단위·구간 목록.
        /// </summary>
        private static IEnumerable<(UsageUnit Unit, DateTime Bucket)> TargetBuckets(DateTimeOffset time, DateTime now, Dictionary<UsageUnit, DateTime> finalized)
        {
            DateTime kst = KstClock.ToKst(time);
            foreach (UsageUnit unit in UsageUnitExtensions.All)
            {
                DateTime bucket = BucketCalculator.Floor(unit, kst);
                if (!RetentionPolicy.IsRetained(unit, bucket, now))
                    continue;
                if (unit == UsageUnit.Minute || (finalized.TryGetValue(unit, out DateTime until) && bucket < until))
                    yield return (unit, bucket);
            }
        }

        /// <summary>
        /// 유지 작업: 시→일→주→월→년 순으로 확정 경계 뒤 구간을 하위 단위에서 다시 계산하고 확정 경계를 현재 구간 시작으로 옮긴 뒤,
        /// 보관 기간이 지났고 직접 상위 단위가 확정된 행을 지운다. 한 트랜잭션으로 처리한다.
        /// </summary>
        public async Task RunMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                DateTime now = KstClock.Now(timeProvider);
                using DbTransaction transaction = connection.BeginTransaction();
                Dictionary<UsageUnit, DateTime> finalized = LoadFinalized(transaction);

                foreach (UsageUnit unit in UsageUnitExtensions.RolledUp)
                {
                    UsageUnit child = unit.Child()!.Value;
                    DateTime? from = finalized.TryGetValue(unit, out DateTime until) ? until : EarliestBucket(transaction, child);
                    if (from is not null)
                    {
                        DateTime start = BucketCalculator.Floor(unit, from.Value);
                        string part = unit.ToName();
                        Execute(transaction, "DELETE FROM token_usage WHERE unit = $unit AND bucket_start >= $from",
                            ("unit", unit.ToName()), ("from", start));
                        Execute(transaction, $"""
                            INSERT INTO token_usage
                            SELECT $unit, date_trunc('{part}', bucket_start), user_id, model, token_type, CAST(SUM(tokens) AS BIGINT)
                            FROM token_usage WHERE unit = $child AND bucket_start >= $from
                            GROUP BY 2, 3, 4, 5
                            """,
                            ("unit", unit.ToName()), ("child", child.ToName()), ("from", start));
                        Execute(transaction, "DELETE FROM cost_usage WHERE unit = $unit AND bucket_start >= $from",
                            ("unit", unit.ToName()), ("from", start));
                        Execute(transaction, $"""
                            INSERT INTO cost_usage
                            SELECT $unit, date_trunc('{part}', bucket_start), user_id, model, SUM(cost_usd)
                            FROM cost_usage WHERE unit = $child AND bucket_start >= $from
                            GROUP BY 2, 3, 4
                            """,
                            ("unit", unit.ToName()), ("child", child.ToName()), ("from", start));
                    }

                    DateTime current = BucketCalculator.Floor(unit, now);
                    Execute(transaction, """
                        INSERT INTO rollup_state VALUES ($unit, $until)
                        ON CONFLICT (unit) DO UPDATE SET finalized_until = EXCLUDED.finalized_until
                        """,
                        ("unit", unit.ToName()), ("until", current));
                    finalized[unit] = current;
                }

                foreach (UsageUnit unit in UsageUnitExtensions.All)
                {
                    DateTime? cutoff = RetentionPolicy.Cutoff(unit, now);
                    if (cutoff is null)
                        continue;

                    // 직접 상위 단위가 확정한 구간까지만 지운다(재계산은 바로 위 단위만 하위 행을 읽음).
                    DateTime bound = cutoff.Value;
                    foreach (UsageUnit parent in unit.DirectParents())
                    {
                        if (finalized[parent] < bound)
                            bound = finalized[parent];
                    }

                    Execute(transaction, "DELETE FROM token_usage WHERE unit = $unit AND bucket_start < $bound",
                        ("unit", unit.ToName()), ("bound", bound));
                    Execute(transaction, "DELETE FROM cost_usage WHERE unit = $unit AND bucket_start < $bound",
                        ("unit", unit.ToName()), ("bound", bound));
                }

                transaction.Commit();
            }
            finally
            {
                gate.Release();
            }
        }

        private DateTime? EarliestBucket(DbTransaction transaction, UsageUnit unit)
        {
            object? value = Scalar(transaction, """
                SELECT MIN(bucket_start) FROM (
                  SELECT bucket_start FROM token_usage WHERE unit = $unit
                  UNION ALL
                  SELECT bucket_start FROM cost_usage WHERE unit = $unit)
                """, ("unit", unit.ToName()));
            return value is DateTime time ? time : null;
        }

        private Dictionary<UsageUnit, DateTime> LoadFinalized(DbTransaction transaction)
        {
            Dictionary<UsageUnit, DateTime> result = [];
            using DbCommand command = CreateCommand(transaction, "SELECT unit, finalized_until FROM rollup_state");
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (UsageUnitExtensions.TryParse(reader.GetString(0), out UsageUnit unit))
                    result[unit] = reader.GetDateTime(1);
            }
            return result;
        }

        /// <summary>
        /// 구간별 합계를 조회한다. 기간은 시작을 구간 시작으로 내리고 끝을 구간 경계로 올린 뒤(경계면 그대로),
        /// 보관 하한과 현재 구간 끝으로 자른다. 그 범위의 구간은 사용량이 없어도 0으로 채운다.
        /// 토큰 종류 필터가 있으면 비용은 null이다.
        /// </summary>
        public async Task<UsageSeries> QuerySeriesAsync(UsageQuery query, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                DateTime now = KstClock.Now(timeProvider);
                DateTime from = BucketCalculator.Floor(query.Unit, query.FromKst);
                DateTime to = BucketCalculator.Ceil(query.Unit, query.ToKst);
                DateTime? retainedFrom = RetentionPolicy.RetainedFrom(query.Unit, now);
                DateTime start = retainedFrom is not null && retainedFrom.Value > from ? retainedFrom.Value : from;
                DateTime currentEnd = BucketCalculator.Next(query.Unit, BucketCalculator.Floor(query.Unit, now));
                DateTime end = to < currentEnd ? to : currentEnd;
                bool includeCost = query.TokenTypes.Count == 0;

                Dictionary<DateTime, Dictionary<string, long>> tokens = [];
                Dictionary<DateTime, double> costs = [];
                SortedSet<string> series = new(StringComparer.Ordinal);

                if (start < end)
                {
                    List<(string, object?)> parameters = [("unit", query.Unit.ToName()), ("start", start), ("end", end)];
                    string tokenFilter = InFilter("user_id", "u", query.Users, parameters)
                        + InFilter("model", "m", query.Models, parameters)
                        + InFilter("token_type", "t", query.TokenTypes, parameters);
                    string column = query.GroupBy.ToColumn();
                    using (DbCommand command = CreateCommand(null, $"""
                        SELECT bucket_start, {column}, CAST(SUM(tokens) AS BIGINT)
                        FROM token_usage
                        WHERE unit = $unit AND bucket_start >= $start AND bucket_start < $end{tokenFilter}
                        GROUP BY 1, 2
                        """, [.. parameters]))
                    using (DbDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            DateTime bucket = reader.GetDateTime(0);
                            string key = reader.GetString(1);
                            long value = reader.GetInt64(2);
                            if (!tokens.TryGetValue(bucket, out Dictionary<string, long>? values))
                                tokens[bucket] = values = new Dictionary<string, long>(StringComparer.Ordinal);
                            values[key] = value;
                            series.Add(key);
                        }
                    }

                    if (includeCost)
                    {
                        List<(string, object?)> costParameters = [("unit", query.Unit.ToName()), ("start", start), ("end", end)];
                        string costFilter = InFilter("user_id", "u", query.Users, costParameters)
                            + InFilter("model", "m", query.Models, costParameters);
                        using DbCommand command = CreateCommand(null, $"""
                            SELECT bucket_start, SUM(cost_usd)
                            FROM cost_usage
                            WHERE unit = $unit AND bucket_start >= $start AND bucket_start < $end{costFilter}
                            GROUP BY 1
                            """, [.. costParameters]);
                        using DbDataReader reader = command.ExecuteReader();
                        while (reader.Read())
                            costs[reader.GetDateTime(0)] = reader.GetDouble(1);
                    }
                }

                List<UsageBucket> buckets = [];
                long totalTokens = 0;
                double totalCost = 0;
                for (DateTime bucket = start; bucket < end; bucket = BucketCalculator.Next(query.Unit, bucket))
                {
                    IReadOnlyDictionary<string, long> values = tokens.TryGetValue(bucket, out Dictionary<string, long>? found)
                        ? found
                        : new Dictionary<string, long>();
                    long bucketTokens = values.Values.Sum();
                    double? bucketCost = includeCost ? costs.GetValueOrDefault(bucket) : null;
                    buckets.Add(new UsageBucket(bucket, values, bucketTokens, bucketCost));
                    totalTokens += bucketTokens;
                    totalCost += bucketCost ?? 0;
                }

                return new UsageSeries(query.Unit, from, to, query.GroupBy, [.. series], buckets, totalTokens, includeCost ? totalCost : null);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 단위별 보관 하한과 저장된 사용자·모델·토큰 종류 목록.
        /// </summary>
        public async Task<UsageMeta> QueryMetaAsync(CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                DateTime now = KstClock.Now(timeProvider);
                List<UsageUnitRetention> units = [.. UsageUnitExtensions.All.Select(unit => new UsageUnitRetention(unit, RetentionPolicy.RetainedFrom(unit, now)))];
                return new UsageMeta(
                    now,
                    units,
                    Distinct("SELECT user_id FROM token_usage UNION SELECT user_id FROM cost_usage"),
                    Distinct("SELECT model FROM token_usage UNION SELECT model FROM cost_usage"),
                    Distinct("SELECT DISTINCT token_type FROM token_usage"));
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// 테스트용: 단위의 토큰 행(구간, 사용자, 모델, 토큰 종류, 토큰)과 비용 행(구간, 사용자, 모델, 비용)을 읽는다.
        /// </summary>
        internal async Task<(List<(DateTime Bucket, string User, string Model, string Type, long Tokens)> Tokens, List<(DateTime Bucket, string User, string Model, double Cost)> Costs)> ReadRowsAsync(UsageUnit unit)
        {
            await gate.WaitAsync();
            try
            {
                List<(DateTime, string, string, string, long)> tokens = [];
                using (DbCommand command = CreateCommand(null, "SELECT bucket_start, user_id, model, token_type, tokens FROM token_usage WHERE unit = $unit ORDER BY ALL", ("unit", unit.ToName())))
                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        tokens.Add((reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(4)));
                }

                List<(DateTime, string, string, double)> costs = [];
                using (DbCommand command = CreateCommand(null, "SELECT bucket_start, user_id, model, cost_usd FROM cost_usage WHERE unit = $unit ORDER BY ALL", ("unit", unit.ToName())))
                using (DbDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        costs.Add((reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetDouble(3)));
                }
                return (tokens, costs);
            }
            finally
            {
                gate.Release();
            }
        }

        private List<string> Distinct(string sql)
        {
            List<string> values = [];
            using DbCommand command = CreateCommand(null, $"SELECT * FROM ({sql}) ORDER BY 1");
            using DbDataReader reader = command.ExecuteReader();
            while (reader.Read())
                values.Add(reader.GetString(0));
            return values;
        }

        private static string InFilter(string column, string prefix, IReadOnlyList<string> values, List<(string, object?)> parameters)
        {
            if (values.Count == 0)
                return string.Empty;
            List<string> names = [];
            for (int i = 0; i < values.Count; i++)
            {
                string name = $"{prefix}{i}";
                names.Add("$" + name);
                parameters.Add((name, values[i]));
            }
            return $" AND {column} IN ({string.Join(", ", names)})";
        }

        private DbCommand CreateCommand(DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
        {
            DuckDBCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = (DuckDBTransaction?)transaction;
            foreach ((string name, object? value) in parameters)
                command.Parameters.Add(new DuckDBParameter(name, value ?? DBNull.Value));
            return command;
        }

        private void Execute(DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
        {
            using DbCommand command = CreateCommand(transaction, sql, parameters);
            command.ExecuteNonQuery();
        }

        private object? Scalar(DbTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
        {
            using DbCommand command = CreateCommand(transaction, sql, parameters);
            object? value = command.ExecuteScalar();
            return value is DBNull ? null : value;
        }

        public void Dispose()
        {
            connection.Dispose();
            gate.Dispose();
        }
    }
}
