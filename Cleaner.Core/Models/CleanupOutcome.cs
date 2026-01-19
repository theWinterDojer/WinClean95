namespace Cleaner.Core.Models;

public sealed record CleanupOutcome(
    string FindingId,
    bool Success,
    string Message,
    long BytesReclaimed,
    string ReasonCategory);
