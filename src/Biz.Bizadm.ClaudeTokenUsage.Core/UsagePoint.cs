namespace Biz.Bizadm.ClaudeTokenUsage.Core
{
    /// <summary>
    /// <c>claude_code.token.usage</c> 델타 데이터 포인트 하나. <see cref="Time"/>은 포인트의 끝 시각(time_unix_nano)이다.
    /// </summary>
    public sealed record TokenUsagePoint(DateTimeOffset Time, string User, string Model, string TokenType, long Tokens);

    /// <summary>
    /// <c>claude_code.cost.usage</c> 델타 데이터 포인트 하나(USD). <see cref="Time"/>은 포인트의 끝 시각이다.
    /// </summary>
    public sealed record CostUsagePoint(DateTimeOffset Time, string User, string Model, double CostUsd);
}
