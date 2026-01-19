namespace Cleaner.App.Settings;

public sealed class AppSettings
{
    public Dictionary<string, bool> CategoryEnabled { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool SafetyRulesExpanded { get; set; }
    public List<string> SelectedDrives { get; set; } = new();
}
