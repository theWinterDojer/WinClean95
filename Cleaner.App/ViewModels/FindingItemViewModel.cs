using Cleaner.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

public sealed partial class FindingItemViewModel : ObservableObject
{
    private readonly Finding _finding;

    [ObservableProperty]
    private bool _isSelected;

    public string Id => _finding.Id;
    public string CategoryName { get; }
    public string CategoryId => _finding.CategoryId;
    public string DriveRoot => _finding.DriveRoot;
    public string? Path => _finding.Path;
    public string SizeDisplay => FormatSize(_finding.SizeBytes);
    public string LastWriteDisplay => _finding.LastWriteTimeUtc?.ToLocalTime().ToString("g") ?? "n/a";
    public string Reason => _finding.Reason;
    public long SizeBytes => _finding.SizeBytes;
    public Finding Finding => _finding;

    public FindingItemViewModel(Finding finding, bool isSelected)
    {
        _finding = finding;
        CategoryName = Categories.All.FirstOrDefault(c => string.Equals(c.Id, finding.CategoryId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? finding.CategoryId;
        _isSelected = isSelected;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var size = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        double display = size;
        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return $"{display:0.##} {units[unitIndex]}";
    }
}
