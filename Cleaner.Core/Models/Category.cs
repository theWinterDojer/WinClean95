namespace Cleaner.Core.Models;

public sealed record Category(
    string Id,
    string Name,
    string Description,
    bool DefaultEnabled,
    string RiskLevel);
