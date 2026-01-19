namespace Cleaner.Core.Models;

public static class CleanupOutcomeCategories
{
    public const string Deleted = "deleted";
    public const string SkippedLocked = "skipped.locked";
    public const string SkippedAccessDenied = "skipped.access";
    public const string SkippedTooNew = "skipped.too-new";
    public const string SkippedCompatibility = "skipped.compatibility";
    public const string SkippedSafetyRecheck = "skipped.safety";
    public const string SkippedOther = "skipped.other";
}
