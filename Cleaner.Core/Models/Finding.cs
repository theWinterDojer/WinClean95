namespace Cleaner.Core.Models;

public sealed record Finding(
    string Id,
    string CategoryId,
    string ProviderId,
    string DriveRoot,
    string? Path,
    long SizeBytes,
    DateTime? LastWriteTimeUtc,
    string Confidence,
    string Reason,
    bool RequiresAdmin,
    bool RequiresAppClosed,
    string RecommendedAction);
