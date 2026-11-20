namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// Claude Code 지표 이름·속성 이름과 속성 찾기 규칙.
    /// 속성은 데이터 포인트 속성에서 먼저 찾고, 없으면 리소스 속성에서 찾는다.
    /// </summary>
    public static class UsageAttributes
    {
        public const string TokenUsageMetric = "claude_code.token.usage";
        public const string CostUsageMetric = "claude_code.cost.usage";

        public const string UserEmail = "user.email";
        public const string UserAccountUuid = "user.account_uuid";
        public const string UserId = "user.id";
        public const string Model = "model";
        public const string Type = "type";

        public const string Unknown = "unknown";

        /// <summary>
        /// 사용자: <c>user.email</c> → <c>user.account_uuid</c> → <c>user.id</c> 순으로 처음 찾은 값, 없으면 <c>unknown</c>.
        /// </summary>
        public static string ResolveUser(IReadOnlyDictionary<string, string> pointAttributes, IReadOnlyDictionary<string, string> resourceAttributes)
            => Resolve(pointAttributes, resourceAttributes, UserEmail, UserAccountUuid, UserId);

        /// <summary>
        /// 모델: <c>model</c>, 없으면 <c>unknown</c>.
        /// </summary>
        public static string ResolveModel(IReadOnlyDictionary<string, string> pointAttributes, IReadOnlyDictionary<string, string> resourceAttributes)
            => Resolve(pointAttributes, resourceAttributes, Model);

        /// <summary>
        /// 토큰 종류: <c>type</c>, 없으면 <c>unknown</c>.
        /// </summary>
        public static string ResolveTokenType(IReadOnlyDictionary<string, string> pointAttributes, IReadOnlyDictionary<string, string> resourceAttributes)
            => Resolve(pointAttributes, resourceAttributes, Type);

        /// <summary>
        /// 키 순서대로, 키마다 포인트 속성 → 리소스 속성에서 비어 있지 않은 값을 찾는다.
        /// </summary>
        internal static string Resolve(IReadOnlyDictionary<string, string> pointAttributes, IReadOnlyDictionary<string, string> resourceAttributes, params string[] keys)
        {
            foreach (string key in keys)
            {
                if (pointAttributes.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value))
                    return value;
                if (resourceAttributes.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            return Unknown;
        }
    }
}
