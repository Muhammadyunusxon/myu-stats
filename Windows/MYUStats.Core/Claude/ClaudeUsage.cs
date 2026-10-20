namespace MYUStats;

/// <summary>One of Claude Code's usage limits: how much of it is used, and when it starts over.</summary>
public sealed record UsageWindow(string Id, string Label, double UsedFraction, DateTimeOffset? ResetsAt);

public enum ClaudeUsageStatus
{
    Ok,
    /// <summary>Nothing to show: Claude Code did not answer. The message says what to do.</summary>
    Unavailable,
    /// <summary>The last read failed; the windows are from an earlier one.</summary>
    Stale,
}

/// <summary>Claude Code's limits as /usage last reported them.</summary>
public sealed record ClaudeUsage(
    ClaudeUsageStatus Status,
    IReadOnlyList<UsageWindow> Windows,
    string? Plan,
    DateTimeOffset? UpdatedAt,
    string? Message = null)
{
    /// <summary>The ring shows the limit closest to running out: a weekly limit at 95 % matters more than a session at 10 %.</summary>
    public UsageWindow? Headline => Windows.Count == 0 ? null : Windows.MaxBy(window => window.UsedFraction);

    public bool Equals(ClaudeUsage? other) =>
        other is not null && Status == other.Status && Windows.SequenceEqual(other.Windows) && Plan == other.Plan
        && UpdatedAt == other.UpdatedAt && Message == other.Message;

    public override int GetHashCode() => HashCode.Combine(Status, Windows.Count, Plan, UpdatedAt, Message);
}

public enum ClaudeUsageError
{
    /// <summary>The command exited with an error or timed out; in practice, no sign-in.</summary>
    NoAnswer,
    /// <summary>It answered, but without the lines this reads.</summary>
    Unreadable,
}

public sealed class ClaudeUsageException(ClaudeUsageError error) : Exception($"Claude Code usage: {error}")
{
    public ClaudeUsageError Error { get; } = error;
}

public sealed record ClaudeReading(IReadOnlyList<UsageWindow> Windows, string? Plan);

/// <summary>Where readings come from; a fake stands in for it in tests.</summary>
public interface IClaudeUsageSource
{
    /// <summary>Whether Claude Code is installed where this can run it.</summary>
    bool IsInstalled { get; }

    Task<ClaudeReading> ReadAsync(CancellationToken cancellationToken = default);
}
