namespace Cleaner.Core.Models;

public sealed record CleanupProgress(
    int ProcessedCount,
    int TotalCount,
    int DeletedCount,
    int SkippedCount);
