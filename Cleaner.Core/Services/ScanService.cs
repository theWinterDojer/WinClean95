using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Services;

public sealed class ScanService
{
    public async Task<ScanResult> ScanAsync(IEnumerable<IProvider> providers, SafetyPolicy policy, CancellationToken ct)
    {
        var findings = new List<Finding>();

        foreach (var provider in providers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var providerFindings = await provider.ScanAsync(policy, ct).ConfigureAwait(false);
                findings.AddRange(providerFindings);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                policy.ReportWarning($"{provider.Id} scan failed: {ex.Message}");
            }
        }

        var totalByCategory = findings
            .GroupBy(finding => finding.CategoryId)
            .ToDictionary(group => group.Key, group => group.Sum(f => f.SizeBytes), StringComparer.OrdinalIgnoreCase);

        var totalByDrive = findings
            .GroupBy(finding => finding.DriveRoot)
            .ToDictionary(group => group.Key, group => group.Sum(f => f.SizeBytes), StringComparer.OrdinalIgnoreCase);

        return new ScanResult(findings, totalByCategory, totalByDrive);
    }
}
