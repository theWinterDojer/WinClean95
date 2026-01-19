using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

public sealed partial class CleanupConfirmationViewModel : ObservableObject
{
    public sealed record CategoryRow(string Name, int Count, long Bytes, string BytesDisplay);

    private const int LargeSelectionWarningThreshold = 1000;

    private readonly IReadOnlyList<FindingItemViewModel> _items;

    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private string _selectedBytesDisplay = "0 B";

    [ObservableProperty]
    private string _driveSummary = "None";

    [ObservableProperty]
    private bool _showLargeSelectionWarning;

    public event Action<CleanupDecision>? RequestClose;

    public enum CleanupDecision
    {
        Cancel,
        Recycle,
        Permanent
    }

    public CleanupConfirmationViewModel(IReadOnlyList<FindingItemViewModel> items)
    {
        _items = items;

        Recalculate();
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke(CleanupDecision.Cancel);
    }

    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void MoveToRecycleBin()
    {
        RequestClose?.Invoke(CleanupDecision.Recycle);
    }

    [RelayCommand(CanExecute = nameof(CanProceed))]
    private void PermanentlyDelete()
    {
        RequestClose?.Invoke(CleanupDecision.Permanent);
    }

    private bool CanProceed()
    {
        if (SelectedCount == 0)
        {
            return false;
        }
        return true;
    }

    private void Recalculate()
    {
        var selectedItems = _items.Where(item => item.IsSelected).ToList();
        SelectedCount = selectedItems.Count;
        var selectedBytes = selectedItems.Sum(item => item.SizeBytes);
        SelectedBytesDisplay = FormatSize(selectedBytes);
        ShowLargeSelectionWarning = SelectedCount >= LargeSelectionWarningThreshold;

        CategoryRows.Clear();
        foreach (var group in selectedItems
                     .GroupBy(item => item.CategoryName)
                     .OrderByDescending(group => group.Sum(item => item.SizeBytes))
                     .ThenBy(group => group.Key))
        {
            var bytes = group.Sum(item => item.SizeBytes);
            CategoryRows.Add(new CategoryRow(group.Key, group.Count(), bytes, FormatSize(bytes)));
        }

        DriveSummary = FormatDriveSummary(selectedItems
            .Select(item => item.DriveRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToList());

        MoveToRecycleBinCommand.NotifyCanExecuteChanged();
        PermanentlyDeleteCommand.NotifyCanExecuteChanged();
    }

    private static string FormatDriveSummary(IReadOnlyList<string> drives)
    {
        if (drives.Count == 0)
        {
            return "None";
        }

        const int maxShown = 4;
        var shown = drives.Take(maxShown).ToList();
        if (drives.Count > maxShown)
        {
            return $"{string.Join(", ", shown)} (+{drives.Count - maxShown} more)";
        }

        return string.Join(", ", shown);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var unitIndex = 0;
        double display = bytes;
        while (display >= 1024 && unitIndex < units.Length - 1)
        {
            display /= 1024;
            unitIndex++;
        }

        return $"{display:0.##} {units[unitIndex]}";
    }
}
