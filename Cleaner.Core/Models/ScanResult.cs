namespace Cleaner.Core.Models;

public sealed class ScanResult
{
    public IReadOnlyList<Finding> Findings { get; }
    public IReadOnlyDictionary<string, long> TotalBytesByCategory { get; }
    public IReadOnlyDictionary<string, long> TotalBytesByDrive { get; }

    public ScanResult(
        IReadOnlyList<Finding> findings,
        IReadOnlyDictionary<string, long> totalBytesByCategory,
        IReadOnlyDictionary<string, long> totalBytesByDrive)
    {
        Findings = findings;
        TotalBytesByCategory = totalBytesByCategory;
        TotalBytesByDrive = totalBytesByDrive;
    }
}
