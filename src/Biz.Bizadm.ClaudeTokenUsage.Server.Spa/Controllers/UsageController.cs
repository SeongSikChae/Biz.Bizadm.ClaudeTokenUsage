using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using Biz.Bizadm.ClaudeTokenUsage.Core;
using Biz.Bizadm.ClaudeTokenUsage.Infrastructure.Storage;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Spa.Controllers
{
    /// <summary>
    /// 사용량 조회 API.
    /// </summary>
    [ApiController]
    [Route("api/usage")]
    public sealed class UsageController : ControllerBase
    {
        private readonly DuckDbUsageStore store;

        public UsageController(DuckDbUsageStore store)
        {
            this.store = store;
        }

        /// <summary>
        /// 구간별 토큰·비용 합계. <paramref name="from"/>·<paramref name="to"/>는 ISO 8601이며 오프셋이 없으면 KST로 본다.
        /// </summary>
        [HttpGet("series")]
        public async Task<IActionResult> GetSeriesAsync(
            [FromQuery] string? unit,
            [FromQuery] string? from,
            [FromQuery] string? to,
            [FromQuery] string? groupBy,
            [FromQuery(Name = "user")] string[]? users,
            [FromQuery(Name = "model")] string[]? models,
            [FromQuery(Name = "tokenType")] string[]? tokenTypes,
            CancellationToken cancellationToken)
        {
            if (!UsageUnitExtensions.TryParse(unit, out UsageUnit usageUnit))
                return BadRequestProblem("unit must be one of minute, hour, day, week, month, year.");
            if (!UsageGroupByExtensions.TryParse(groupBy, out UsageGroupBy usageGroupBy))
                return BadRequestProblem("groupBy must be one of tokenType, model, user.");
            if (!TryParseKst(from, out DateTime fromKst))
                return BadRequestProblem("from must be an ISO 8601 date-time.");
            if (!TryParseKst(to, out DateTime toKst))
                return BadRequestProblem("to must be an ISO 8601 date-time.");
            if (fromKst >= toKst)
                return BadRequestProblem("from must be earlier than to.");

            UsageQuery query = new(usageUnit, fromKst, toKst, usageGroupBy, Clean(users), Clean(models), Clean(tokenTypes));
            UsageSeries series = await store.QuerySeriesAsync(query, cancellationToken);
            return Ok(SeriesResponse.Create(series));
        }

        /// <summary>
        /// 단위별 보관 하한과 필터 목록.
        /// </summary>
        [HttpGet("meta")]
        public async Task<IActionResult> GetMetaAsync(CancellationToken cancellationToken)
        {
            UsageMeta meta = await store.QueryMetaAsync(cancellationToken);
            return Ok(new MetaResponse(
                KstClock.FromKst(meta.NowKst),
                [.. meta.Units.Select(u => new UnitResponse(u.Unit.ToName(), u.EarliestKst is null ? null : KstClock.FromKst(u.EarliestKst.Value)))],
                meta.Users,
                meta.Models,
                meta.TokenTypes));
        }

        private ObjectResult BadRequestProblem(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest, detail: detail);

        private static IReadOnlyList<string> Clean(string[]? values)
            => values is null ? [] : [.. values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)];

        /// <summary>
        /// ISO 8601 시각을 KST 벽시계로 바꾼다. 오프셋(또는 Z)이 있으면 그것으로 해석하고, 없으면 KST로 본다.
        /// </summary>
        internal static bool TryParseKst(string? text, out DateTime kst)
        {
            kst = default;
            if (string.IsNullOrWhiteSpace(text)
                || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return false;

            if (parsed.Kind == DateTimeKind.Unspecified)
            {
                kst = parsed;
                return true;
            }

            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset withOffset))
                return false;
            kst = KstClock.ToKst(withOffset);
            return true;
        }

        public sealed record BucketResponse(DateTimeOffset Start, IReadOnlyDictionary<string, long> Tokens, long TotalTokens, double? CostUsd);

        public sealed record TotalsResponse(long Tokens, double? CostUsd);

        public sealed record SeriesResponse(
            string Unit,
            DateTimeOffset From,
            DateTimeOffset To,
            string GroupBy,
            IReadOnlyList<string> Series,
            IReadOnlyList<BucketResponse> Buckets,
            TotalsResponse Totals)
        {
            public static SeriesResponse Create(UsageSeries series) => new(
                series.Unit.ToName(),
                KstClock.FromKst(series.FromKst),
                KstClock.FromKst(series.ToKst),
                series.GroupBy.ToName(),
                series.Series,
                [.. series.Buckets.Select(b => new BucketResponse(KstClock.FromKst(b.StartKst), b.Tokens, b.TotalTokens, b.CostUsd))],
                new TotalsResponse(series.TotalTokens, series.TotalCostUsd));
        }

        public sealed record UnitResponse(string Unit, DateTimeOffset? Earliest);

        public sealed record MetaResponse(
            DateTimeOffset Now,
            IReadOnlyList<UnitResponse> Units,
            IReadOnlyList<string> Users,
            IReadOnlyList<string> Models,
            IReadOnlyList<string> TokenTypes);
    }
}
