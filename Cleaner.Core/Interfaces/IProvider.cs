using Cleaner.Core.Models;
using Cleaner.Core.Safety;

namespace Cleaner.Core.Interfaces;

public interface IProvider
{
    string Id { get; }
    Category Category { get; }
    Task<IReadOnlyList<Finding>> ScanAsync(SafetyPolicy policy, CancellationToken ct);
}
