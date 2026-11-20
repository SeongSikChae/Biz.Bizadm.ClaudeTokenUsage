using Microsoft.Extensions.Logging;

namespace Biz.Bizadm.ClaudeTokenUsage.Server.Core.Tests;

/// <summary>
/// 경고 기록을 확인하기 위한 테스트용 로거.
/// </summary>
internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    public int WarningCount => Entries.Count(entry => entry.Level == LogLevel.Warning);
}
