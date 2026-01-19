using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Cleaner.App.Settings;
using Cleaner.App.Views;
using Cleaner.Core.Interfaces;
using Cleaner.Core.Models;
using Cleaner.Core.Rules;
using Cleaner.Core.Services;
using Cleaner.Core.Safety;
using Cleaner.Windows.Providers;
using Cleaner.Windows.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cleaner.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const int MinRetentionDays = 1;
    private const int MaxRetentionDays = 7;

    private readonly SafetyPolicy _policy;
    private readonly ScanService _scanService;
    private readonly CleanupService _cleanupService;
    private readonly RecycleBinService _recycleBinService;
    private readonly List<IProvider> _providers;
    private int _activeOperations;
    private readonly Stopwatch _actionStopwatch = new();
    private readonly AppSettings _settings;
    private bool _suppressSettings;
    private bool _suppressSelectionSummary;
    private bool _suppressAllFindingsSelectionUpdate;
    private CancellationTokenSource? _operationCts;
    private string? _cancelStatusMessage;
    private ScanResult? _lastScanResult;

    [ObservableProperty]
    private string _statusMessage = "Ready to scan.";

    [ObservableProperty]
    private string _scanDuration = string.Empty;

    [ObservableProperty]
    private string _safetyWarnings = string.Empty;

    [ObservableProperty]
    private string _recentGuardHoursDisplay = string.Empty;

    [ObservableProperty]
    private string _primaryRetentionDaysDisplay = string.Empty;

    [ObservableProperty]
    private int _retentionDays = 5;

    [ObservableProperty]
    private string _compatibilityModeDisplay = string.Empty;

    [ObservableProperty]
    private string _compatibilityDetailsSuffix = string.Empty;

    [ObservableProperty]
    private string _totalBytesDisplay = "Total: 0 B";

    [ObservableProperty]
    private string _selectedBytesDisplay = "Selected: 0 B";

    [ObservableProperty]
    private string _findingsSummary = "0 out of 0 files selected.";

    [ObservableProperty]
    private bool _safetyRulesExpanded;

    [ObservableProperty]
    private bool _isWorking;

    [ObservableProperty]
    private bool _hasSelectedDrives;

    [ObservableProperty]
    private bool? _allFindingsSelected = false;

    [ObservableProperty]
    private bool _isProgressIndeterminate = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _cleanupSummaryLine = "No cleanup results yet.";

    public ObservableCollection<CategoryItemViewModel> CategoryItems { get; } = new();
    public ObservableCollection<FindingItemViewModel> FindingItems { get; } = new();
    public ObservableCollection<DriveItemViewModel> DriveItems { get; } = new();
    public ObservableCollection<RetentionRow> RetentionRows { get; } = new();

    private static readonly string[] CategoryOrder =
    {
        Categories.UserTemp.Id,
        Categories.SystemTemp.Id,
        Categories.WindowsUpdateCache.Id,
        Categories.ThumbnailCache.Id,
        Categories.DirectXShaderCache.Id,
        Categories.BrowserCache.Id,
        Categories.WerReports.Id
    };

    public MainViewModel()
    {
        _policy = new SafetyPolicy();
        _scanService = new ScanService();
        _recycleBinService = new RecycleBinService();
        _cleanupService = new CleanupService(new ICleanupAction[]
        {
            new RecycleDeleteAction(),
            new PermanentDeleteAction(),
            new EmptyRecycleBinAction(_recycleBinService)
        });

        ApplyRetentionDays(_retentionDays);

        _providers = new List<IProvider>
        {
            new UserTempProvider(),
            new SystemTempProvider(),
            new WindowsUpdateCacheProvider(),
            new ThumbnailCacheProvider(),
            new DirectXShaderCacheProvider(),
            new BrowserCacheProvider(),
            new WerReportsProvider()
        };

        var loadedSettings = (AppSettings?)null;
        _settings = loadedSettings ?? new AppSettings();
        _suppressSettings = true;

        var categoriesById = Categories.All.ToDictionary(category => category.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var categoryId in CategoryOrder)
        {
            if (!categoriesById.TryGetValue(categoryId, out var category))
            {
                continue;
            }

            var item = new CategoryItemViewModel(category);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(CategoryItemViewModel.IsSelected))
                {
                    SaveSettings();
                    CleanCommand.NotifyCanExecuteChanged();
                }
            };
            CategoryItems.Add(item);
        }

        LoadDrives(loadedSettings);

        ApplyCategoryDefaults(loadedSettings == null);
        SafetyRulesExpanded = _settings.SafetyRulesExpanded;
        _suppressSettings = false;
        ApplyRetentionDays(_retentionDays);
        UpdatePolicySummary();
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        _ = await ScanInternalAsync("Scanning...", CaptureSelection());
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task CleanAsync()
    {
        var confirmation = new CleanupConfirmationWindow(FindingItems)
        {
            Owner = Application.Current?.MainWindow
        };

        var proceed = confirmation.ShowDialog();
        if (proceed != true || confirmation.Decision == CleanupConfirmationViewModel.CleanupDecision.Cancel)
        {
            StatusMessage = "Cleanup canceled.";
            return;
        }

        var selectedIds = FindingItems.Where(item => item.IsSelected).Select(item => item.Id).ToList();
        if (selectedIds.Count == 0)
        {
            StatusMessage = "No items selected.";
            return;
        }

        var showRecycleNotice = false;
        var useRecycleBin = confirmation.Decision == CleanupConfirmationViewModel.CleanupDecision.Recycle;
        CleanupProgress? lastProgress = null;
        BeginWork();
        var token = BeginCancelableOperation("Canceling cleanup...");
        try
        {
            var findings = FindingItems
                .Select(item => ApplyCleanupDecision(item.Finding, useRecycleBin))
                .ToList();

            var totalSelected = selectedIds.Count;
            var isDeleting = true;
            var progress = new Progress<CleanupProgress>(update =>
            {
                lastProgress = update;
                if (!isDeleting)
                {
                    return;
                }

                StatusMessage = $"Deleting files... {update.ProcessedCount}/{update.TotalCount} processed";
                ProgressValue = update.TotalCount == 0
                    ? 0
                    : Math.Clamp(update.ProcessedCount * 100d / update.TotalCount, 0d, 100d);
            });

            ScanDuration = string.Empty;
            _actionStopwatch.Restart();
            IsProgressIndeterminate = false;
            ProgressValue = 0;

            StatusMessage = $"Deleting files... 0/{totalSelected} processed";

            var outcomes = await Task.Run(async () =>
                await _cleanupService.ExecuteAsync(
                    findings,
                    selectedIds,
                    _policy,
                    token,
                    progress), token);

            isDeleting = false;
            AppendCleanupSummary(outcomes, findings);
            showRecycleNotice = ShouldShowRecycleNotice(outcomes, findings, useRecycleBin);

            StatusMessage = "Refreshing list...";
            var refreshCanceled = await ScanInternalAsync("Refreshing list...", CaptureSelection(), updateActionTime: false);

            StatusMessage = refreshCanceled
                ? "Cleanup finished (refresh canceled)."
                : "Cleanup finished.";
            _actionStopwatch.Stop();
            ScanDuration = $"Action time: {_actionStopwatch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (OperationCanceledException)
        {
            _actionStopwatch.Stop();
            ScanDuration = string.Empty;
            StatusMessage = "Cleanup canceled.";
            SetCanceledCleanupSummary("Cleanup", lastProgress);
            return;
        }
        finally
        {
            EndCancelableOperation();
            EndWork();
        }

        if (showRecycleNotice)
        {
            MessageBox.Show(
                "Files sent to Recycle Bin. Use the Empty Recycle Bin button to permanently delete them.",
                "Recycle Bin",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static bool ShouldShowRecycleNotice(
        IReadOnlyList<CleanupOutcome> outcomes,
        IReadOnlyList<Finding> findings,
        bool useRecycleBin)
    {
        if (!useRecycleBin || outcomes.Count == 0)
        {
            return false;
        }

        var findingById = findings.ToDictionary(finding => finding.Id, finding => finding);
        return ShouldShowRecycleNotice(outcomes, findingById);
    }

    [RelayCommand(CanExecute = nameof(CanEmptyRecycleBin))]
    private async Task EmptyRecycleBinAsync()
    {
        var selectedRoots = GetSelectedDriveRoots();
        if (selectedRoots.Count == 0)
        {
            StatusMessage = "Select at least one drive to empty its Recycle Bin.";
            return;
        }

        var driveList = string.Join(" ", selectedRoots
            .Select(root => root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase));
        var driveLabel = selectedRoots.Count == 1 ? "drive" : "drives";

        var confirmation = MessageBox.Show(
            $"Are you sure you want to empty the Recycle Bin for {driveLabel} {driveList} and permanently delete all files?{Environment.NewLine}{Environment.NewLine}Select additional drives below if needed",
            "Empty Recycle Bin",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "Empty Recycle Bin canceled.";
            return;
        }

        CleanupProgress? lastProgress = null;
        BeginWork();
        var token = BeginCancelableOperation("Canceling Recycle Bin...");
        try
        {
            ScanDuration = string.Empty;
            _actionStopwatch.Restart();
            IsProgressIndeterminate = true;
            ProgressValue = 0;

            StatusMessage = "Emptying Recycle Bin...";

            var findings = new List<Finding>();
            var totalItems = 0L;
            var queryFailures = 0;

            foreach (var root in selectedRoots)
            {
                token.ThrowIfCancellationRequested();
                var sizeBytes = 0L;
                var itemCount = 0L;
                try
                {
                    (sizeBytes, itemCount) = _recycleBinService.Query(root);
                    totalItems += itemCount;
                }
                catch (Exception)
                {
                    queryFailures++;
                }

                _policy.AddAllowlist(ProviderIds.RecycleBin, root);
                findings.Add(new Finding(
                    $"{ProviderIds.RecycleBin}:{Guid.NewGuid():N}",
                    Categories.RecycleBin.Id,
                    ProviderIds.RecycleBin,
                    root,
                    root,
                    sizeBytes,
                    null,
                    "High",
                    itemCount > 0
                        ? $"Recycle Bin contains {itemCount} items."
                        : "Recycle Bin ready to empty.",
                    false,
                    false,
                    CleanupActions.EmptyRecycleBin));
            }

            if (queryFailures == 0 && totalItems == 0)
            {
                StatusMessage = "Recycle Bin is already empty.";
                _actionStopwatch.Stop();
                ScanDuration = $"Action time: {_actionStopwatch.Elapsed.TotalSeconds:0.0}s";
                return;
            }

            var selectedIds = findings.Select(finding => finding.Id).ToList();
            var totalSelected = selectedIds.Count;
            var isEmptying = true;
            var progress = new Progress<CleanupProgress>(update =>
            {
                lastProgress = update;
                if (!isEmptying)
                {
                    return;
                }

                StatusMessage = $"Emptying Recycle Bin... {update.ProcessedCount}/{update.TotalCount} drives processed";
                ProgressValue = update.TotalCount == 0
                    ? 0
                    : Math.Clamp(update.ProcessedCount * 100d / update.TotalCount, 0d, 100d);
            });

            IsProgressIndeterminate = false;
            ProgressValue = 0;
            StatusMessage = $"Emptying Recycle Bin... 0/{totalSelected} drives processed";

            var outcomes = await Task.Run(async () =>
                await _cleanupService.ExecuteAsync(
                    findings,
                    selectedIds,
                    _policy,
                    token,
                    progress), token);

            isEmptying = false;
            AppendCleanupSummary(outcomes, findings);

            StatusMessage = "Recycle Bin emptied.";
            _actionStopwatch.Stop();
            ScanDuration = $"Action time: {_actionStopwatch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (OperationCanceledException)
        {
            _actionStopwatch.Stop();
            ScanDuration = string.Empty;
            StatusMessage = "Empty Recycle Bin canceled.";
            SetCanceledCleanupSummary("Empty Recycle Bin", lastProgress);
            return;
        }
        finally
        {
            EndCancelableOperation();
            EndWork();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_operationCts == null || _operationCts.IsCancellationRequested)
        {
            return;
        }

        StatusMessage = _cancelStatusMessage ?? "Canceling...";
        _operationCts.Cancel();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanCancel()
    {
        return IsWorking && _operationCts is { IsCancellationRequested: false };
    }

    private bool CanClean()
    {
        return FindingItems.Any(item => item.IsSelected);
    }

    private bool CanEmptyRecycleBin()
    {
        return !IsWorking && HasSelectedDrives;
    }

    private void UpdatePolicySummary()
    {
        RecentGuardHoursDisplay = _policy.RecentFileGuardHours.ToString(CultureInfo.InvariantCulture);
        PrimaryRetentionDaysDisplay = SafetyRules.GetRetentionDays(Categories.UserTemp.Id, _policy)
            .ToString(CultureInfo.InvariantCulture);

        CompatibilityModeDisplay = _policy.CompatibilityModeEnabled ? "On" : "Off";
        CompatibilityDetailsSuffix = _policy.CompatibilityModeEnabled
            ? $"; Protects installer/update files for {_policy.CompatibilityInstallerGuardDays} days"
            : string.Empty;

        RetentionRows.Clear();
        RetentionRows.Add(new RetentionRow(
            Categories.UserTemp.Name,
            $"{SafetyRules.GetRetentionDays(Categories.UserTemp.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.SystemTemp.Name,
            $"{SafetyRules.GetRetentionDays(Categories.SystemTemp.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.WindowsUpdateCache.Name,
            $"{SafetyRules.GetRetentionDays(Categories.WindowsUpdateCache.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.ThumbnailCache.Name,
            $"{SafetyRules.GetRetentionDays(Categories.ThumbnailCache.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.DirectXShaderCache.Name,
            $"{SafetyRules.GetRetentionDays(Categories.DirectXShaderCache.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.BrowserCache.Name,
            $"{SafetyRules.GetRetentionDays(Categories.BrowserCache.Id, _policy)} days"));
        RetentionRows.Add(new RetentionRow(
            Categories.WerReports.Name,
            $"{SafetyRules.GetRetentionDays(Categories.WerReports.Id, _policy)} days"));
    }

    private void UpdateWarnings()
    {
        SafetyWarnings = _policy.Warnings.Count == 0
            ? "No safety warnings."
            : string.Join(" ", _policy.Warnings);
    }

    private void UpdateTotals(IReadOnlyList<Finding> findings)
    {
        var totalBytes = findings.Sum(finding => finding.SizeBytes);
        TotalBytesDisplay = $"Total: {FormatSize(totalBytes)}";
    }

    private void UpdateSelectionSummary()
    {
        var selectedBytes = FindingItems.Where(item => item.IsSelected).Sum(item => item.SizeBytes);
        var selectedCount = FindingItems.Count(item => item.IsSelected);
        SelectedBytesDisplay = $"Selected: {FormatSize(selectedBytes)}";
        FindingsSummary = $"{selectedCount} out of {FindingItems.Count} files selected.";

        var previousSuppress = _suppressAllFindingsSelectionUpdate;
        _suppressAllFindingsSelectionUpdate = true;
        if (FindingItems.Count == 0 || selectedCount == 0)
        {
            AllFindingsSelected = false;
        }
        else if (selectedCount == FindingItems.Count)
        {
            AllFindingsSelected = true;
        }
        else
        {
            AllFindingsSelected = null;
        }
        _suppressAllFindingsSelectionUpdate = previousSuppress;
    }

    private void UpdateDriveTotals(ScanResult result)
    {
        foreach (var drive in DriveItems)
        {
            if (result.TotalBytesByDrive.TryGetValue(drive.Root, out var bytes))
            {
                drive.TotalDisplay = FormatSize(bytes);
            }
            else
            {
                drive.TotalDisplay = "0 B";
            }
        }
    }

    private void LoadDrives(AppSettings? loadedSettings)
    {
        DriveItems.Clear();
        var selected = new HashSet<string>(
            loadedSettings?.SelectedDrives ?? new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        var defaultRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))
            ?? "C:\\";

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady))
        {
            var root = drive.RootDirectory.FullName;
            var isSelected = selected.Count == 0
                ? root.Equals(defaultRoot, StringComparison.OrdinalIgnoreCase)
                : selected.Contains(root);
            var item = new DriveItemViewModel(root, isSelected);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DriveItemViewModel.IsSelected))
                {
                    SaveSettings();
                    UpdateHasSelectedDrives();
                    if (_lastScanResult != null)
                    {
                        ApplyDriveFilter(_lastScanResult, CaptureSelection());
                    }
                }
            };
            DriveItems.Add(item);
        }

        UpdateHasSelectedDrives();
    }

    private HashSet<string> GetSelectedDriveRoots()
    {
        return DriveItems
            .Where(item => item.IsSelected)
            .Select(item => item.Root)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void UpdateHasSelectedDrives()
    {
        HasSelectedDrives = DriveItems.Any(item => item.IsSelected);
    }

    private void ApplyDriveFilter(ScanResult result, Dictionary<string, bool>? previousSelection = null)
    {
        UpdateDriveTotals(result);

        var selectedRoots = GetSelectedDriveRoots();
        IReadOnlyList<Finding> filteredFindings;
        if (DriveItems.Count == 0)
        {
            filteredFindings = result.Findings;
        }
        else if (selectedRoots.Count == 0)
        {
            filteredFindings = Array.Empty<Finding>();
        }
        else
        {
            filteredFindings = result.Findings
                .Where(finding => selectedRoots.Contains(finding.DriveRoot))
                .ToList();
        }

        UpdateHasSelectedDrives();

        var selectedCategoryIds = CategoryItems
            .Where(item => item.IsSelected)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var previousSuppressSelection = _suppressSelectionSummary;
        _suppressSelectionSummary = true;
        FindingItems.Clear();
        foreach (var finding in filteredFindings)
        {
            if (string.Equals(finding.CategoryId, Categories.RecycleBin.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isSelected = selectedCategoryIds.Contains(finding.CategoryId);
            if (TryGetPreviousSelection(finding, previousSelection, out var previousSelected))
            {
                isSelected = previousSelected;
            }
            var item = new FindingItemViewModel(finding, isSelected);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(FindingItemViewModel.IsSelected))
                {
                    if (!_suppressSelectionSummary)
                    {
                        UpdateSelectionSummary();
                        CleanCommand.NotifyCanExecuteChanged();
                    }
                }
            };
            FindingItems.Add(item);
        }

        _suppressSelectionSummary = previousSuppressSelection;

        UpdateTotals(filteredFindings);
        UpdateSelectionSummary();
    }

    private async Task<bool> ScanInternalAsync(
        string statusMessage,
        Dictionary<string, bool>? previousSelection,
        bool updateActionTime = true)
    {
        BeginWork();
        try
        {
            if (updateActionTime)
            {
                ScanDuration = string.Empty;
                _actionStopwatch.Restart();
            }

            IsProgressIndeterminate = true;
            ProgressValue = 0;

            StatusMessage = statusMessage;
            _policy.ResetForScan();
            ApplyRetentionDays(RetentionDays);
            UpdatePolicySummary();
            ReportBrowserWarnings();
            CleanCommand.NotifyCanExecuteChanged();

            var enabledCategories = new HashSet<string>(
                CategoryItems.Where(item => item.IsSelected).Select(item => item.Id),
                StringComparer.OrdinalIgnoreCase);

            var providers = _providers.Where(provider => enabledCategories.Contains(provider.Category.Id)).ToList();
            var cancelMessage = updateActionTime ? "Canceling scan..." : "Canceling refresh...";
            var token = BeginCancelableOperation(cancelMessage);

            try
            {
                var result = await Task.Run(async () =>
                    await _scanService.ScanAsync(providers, _policy, token), token);

                if (updateActionTime)
                {
                    _actionStopwatch.Stop();
                    ScanDuration = $"Action time: {_actionStopwatch.Elapsed.TotalSeconds:0.0}s";
                }

                _lastScanResult = result;
                ApplyDriveFilter(result, previousSelection);
                CleanCommand.NotifyCanExecuteChanged();
                UpdateWarnings();
                StatusMessage = "Scan complete.";
            }
            catch (OperationCanceledException)
            {
                if (updateActionTime)
                {
                    _actionStopwatch.Stop();
                    ScanDuration = string.Empty;
                }

                StatusMessage = updateActionTime ? "Scan canceled." : "Refresh canceled.";
                return true;
            }
            catch (Exception ex)
            {
                if (updateActionTime)
                {
                    _actionStopwatch.Stop();
                    ScanDuration = string.Empty;
                }
                StatusMessage = $"Scan failed: {ex.Message}";
            }
            finally
            {
                EndCancelableOperation();
            }
        }
        finally
        {
            EndWork();
        }

        return false;
    }

    private Dictionary<string, bool> CaptureSelection()
    {
        var selection = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in FindingItems)
        {
            var key = BuildSelectionKey(item.Finding);
            if (!string.IsNullOrWhiteSpace(key))
            {
                selection[key] = item.IsSelected;
            }
        }


        return selection;
    }

    private static bool TryGetPreviousSelection(
        Finding finding,
        Dictionary<string, bool>? previousSelection,
        out bool isSelected)
    {
        isSelected = false;
        if (previousSelection == null || previousSelection.Count == 0)
        {
            return false;
        }

        var key = BuildSelectionKey(finding);
        return !string.IsNullOrWhiteSpace(key) && previousSelection.TryGetValue(key, out isSelected);
    }

    private static string BuildCategorySelectionKey(string categoryId)
    {
        return $"category:{categoryId}";
    }

    private static string BuildSelectionKey(Finding finding)
    {
        var path = string.IsNullOrWhiteSpace(finding.Path) ? finding.DriveRoot : finding.Path;
        var normalizedPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                normalizedPath = SafetyRules.NormalizePath(path);
            }
            catch (Exception)
            {
                normalizedPath = path;
            }
        }

        var action = finding.RecommendedAction ?? string.Empty;
        return $"{finding.ProviderId}|{action}|{normalizedPath}";
    }

    private void AppendCleanupSummary(IReadOnlyList<CleanupOutcome> outcomes, IReadOnlyList<Finding> findings)
    {
        if (outcomes.Count == 0)
        {
            CleanupSummaryLine = "No cleanup actions executed.";
            return;
        }

        var findingById = findings.ToDictionary(finding => finding.Id, finding => finding);
        var deleted = outcomes.Where(outcome => outcome.Success).ToList();
        var skipped = outcomes.Where(outcome => !outcome.Success).ToList();

        var deletedBytes = deleted.Sum(outcome => outcome.BytesReclaimed);
        var deletedFromRecycle = deleted.Count(outcome => IsRecycleOutcome(outcome, findingById));
        var deletedPermanent = deleted.Count - deletedFromRecycle;

        var skippedBytes = skipped.Sum(outcome =>
        {
            return findingById.TryGetValue(outcome.FindingId, out var finding) ? finding.SizeBytes : 0;
        });


        if (skipped.Count == 0)
        {
            CleanupSummaryLine = BuildDeletedSummary(deleted, deletedBytes, deletedFromRecycle, deletedPermanent);
            return;
        }

        var lockedCount = skipped.Count(outcome =>
            string.Equals(outcome.ReasonCategory, CleanupOutcomeCategories.SkippedLocked, StringComparison.OrdinalIgnoreCase));
        var accessDeniedCount = skipped.Count(outcome =>
            string.Equals(outcome.ReasonCategory, CleanupOutcomeCategories.SkippedAccessDenied, StringComparison.OrdinalIgnoreCase));
        var tooNewCount = skipped.Count(outcome =>
            string.Equals(outcome.ReasonCategory, CleanupOutcomeCategories.SkippedTooNew, StringComparison.OrdinalIgnoreCase)
            || string.Equals(outcome.ReasonCategory, CleanupOutcomeCategories.SkippedCompatibility, StringComparison.OrdinalIgnoreCase)
            || string.Equals(outcome.ReasonCategory, CleanupOutcomeCategories.SkippedSafetyRecheck, StringComparison.OrdinalIgnoreCase));
        var otherSkippedCount = skipped.Count - lockedCount - accessDeniedCount - tooNewCount;

        var skippedDetails = new List<string>();
        if (lockedCount > 0)
        {
            skippedDetails.Add($"In use: {lockedCount}");
        }

        if (accessDeniedCount > 0)
        {
            skippedDetails.Add($"Access denied: {accessDeniedCount}");
        }

        if (tooNewCount > 0)
        {
            skippedDetails.Add($"Too new: {tooNewCount}");
        }

        if (otherSkippedCount > 0)
        {
            skippedDetails.Add($"Other: {otherSkippedCount}");
        }

        var detailText = skippedDetails.Count > 0
            ? $"{Environment.NewLine}{string.Join("  |  ", skippedDetails)}"
            : string.Empty;

        var deletedSummary = BuildDeletedSummary(deleted, deletedBytes, deletedFromRecycle, deletedPermanent);
        CleanupSummaryLine =
            $"{deletedSummary}    Skipped: {skipped.Count} items / {FormatSize(skippedBytes)}{detailText}";
    }

    private void SetCanceledCleanupSummary(string actionName, CleanupProgress? progress)
    {
        if (progress == null)
        {
            CleanupSummaryLine = $"{actionName} canceled.";
            return;
        }

        CleanupSummaryLine =
            $"{actionName} canceled after {progress.ProcessedCount}/{progress.TotalCount} processed.";
    }

    private static string BuildDeletedSummary(
        IReadOnlyList<CleanupOutcome> deleted,
        long deletedBytes,
        int deletedFromRecycle,
        int deletedPermanent)
    {
        if (deletedFromRecycle > 0 && deletedPermanent > 0)
        {
            return $"Deleted: {deleted.Count} items / {FormatSize(deletedBytes)} (Recycle Bin: {deletedFromRecycle}, Permanent: {deletedPermanent})";
        }

        if (deletedFromRecycle > 0)
        {
            return $"Deleted: {deleted.Count} items / {FormatSize(deletedBytes)} (Recycle Bin: {deletedFromRecycle})";
        }

        if (deletedPermanent > 0)
        {
            return $"Deleted: {deleted.Count} items / {FormatSize(deletedBytes)} (Permanent: {deletedPermanent})";
        }

        return $"Deleted: {deleted.Count} items / {FormatSize(deletedBytes)}";
    }

    private static bool IsRecycleOutcome(
        CleanupOutcome outcome,
        IReadOnlyDictionary<string, Finding> findingById)
    {
        return findingById.TryGetValue(outcome.FindingId, out var finding)
            && string.Equals(finding.RecommendedAction, CleanupActions.RecycleFile, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldShowRecycleNotice(
        IReadOnlyList<CleanupOutcome> outcomes,
        IReadOnlyDictionary<string, Finding> findingById)
    {
        foreach (var outcome in outcomes)
        {
            if (!outcome.Success)
            {
                continue;
            }

            if (findingById.TryGetValue(outcome.FindingId, out var finding)
                && string.Equals(finding.RecommendedAction, CleanupActions.RecycleFile, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Finding ApplyCleanupDecision(Finding finding, bool useRecycleBin)
    {
        if (string.Equals(finding.RecommendedAction, CleanupActions.EmptyRecycleBin, StringComparison.OrdinalIgnoreCase))
        {
            return finding;
        }

        var action = useRecycleBin ? CleanupActions.RecycleFile : CleanupActions.PermanentDeleteFile;
        if (string.Equals(finding.RecommendedAction, action, StringComparison.OrdinalIgnoreCase))
        {
            return finding;
        }

        return finding with { RecommendedAction = action };
    }

    private void ReportBrowserWarnings()
    {
        var running = new List<string>();
        TryAddProcess("msedge", "Microsoft Edge", running);
        TryAddProcess("chrome", "Google Chrome", running);
        TryAddProcess("firefox", "Mozilla Firefox", running);

        if (running.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", running);
        _policy.ReportWarning($"Browser running ({names}). Cache items may be in use.");
    }

    private static void TryAddProcess(string processName, string displayName, ICollection<string> running)
    {
        try
        {
            if (Process.GetProcessesByName(processName).Length > 0)
            {
                running.Add(displayName);
            }
        }
        catch (Exception)
        {
            // Ignore process enumeration failures.
        }
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

    public sealed record RetentionRow(string Name, string DaysDisplay);

    [RelayCommand]
    private void SelectAllCategories()
    {
        SetCategorySelection(true);
    }

    [RelayCommand]
    private void DeselectAllCategories()
    {
        SetCategorySelection(false);
    }

    [RelayCommand]
    private void OpenSystemCleanup()
    {
        var confirmation = MessageBox.Show(
            "Scan for Windows Update files?",
            "System Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "System Cleanup canceled.";
            return;
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var driveArg = systemRoot.TrimEnd(Path.DirectorySeparatorChar);

        try
        {
            var cleanupInfo = new ProcessStartInfo
            {
                FileName = "cleanmgr.exe",
                Arguments = $"/d {driveArg}",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(cleanupInfo);
            StatusMessage = "Opened Windows System Cleanup.";
        }
        catch (Win32Exception)
        {
            OpenStorageSense();
        }
        catch (Exception)
        {
            OpenStorageSense();
        }
    }

    private void OpenStorageSense()
    {
        try
        {
            var settingsInfo = new ProcessStartInfo
            {
                FileName = "ms-settings:storagesense",
                UseShellExecute = true
            };

            Process.Start(settingsInfo);
            StatusMessage = "Opened Storage Sense settings.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Unable to open System Cleanup: {ex.Message}";
        }
    }

    partial void OnAllFindingsSelectedChanged(bool? value)
    {
        if (_suppressAllFindingsSelectionUpdate)
        {
            return;
        }

        if (FindingItems.Count == 0)
        {
            return;
        }

        if (value == true)
        {
            _ = SetFindingSelectionAsync(true, "Selecting files...");
        }
        else if (value == false)
        {
            _ = SetFindingSelectionAsync(false, "Deselecting files...");
        }
    }

    private void SetCategorySelection(bool isSelected)
    {
        var previous = _suppressSettings;
        _suppressSettings = true;
        foreach (var item in CategoryItems)
        {
            item.IsSelected = isSelected;
        }
        _suppressSettings = previous;
        SaveSettings();
    }

    private void ApplyCategoryDefaults(bool firstRun)
    {
        if (firstRun)
        {
            foreach (var item in CategoryItems)
            {
                item.IsSelected = string.Equals(item.Id, Categories.UserTemp.Id, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            foreach (var item in CategoryItems)
            {
                if (_settings.CategoryEnabled.TryGetValue(item.Id, out var enabled))
                {
                    item.IsSelected = enabled;
                }
            }
        }
    }

    partial void OnSafetyRulesExpandedChanged(bool value)
    {
        SaveSettings();
    }

    partial void OnIsWorkingChanged(bool value)
    {
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnRetentionDaysChanged(int value)
    {
        var clamped = Math.Clamp(value, MinRetentionDays, MaxRetentionDays);
        if (clamped != value)
        {
            RetentionDays = clamped;
            return;
        }

        ApplyRetentionDays(clamped);
        UpdatePolicySummary();
        SaveSettings();
    }

    private void SaveSettings()
    {
        // Settings persistence disabled.
    }

    private void ApplyRetentionDays(int retentionDays)
    {
        foreach (var category in Categories.All)
        {
            if (category.Id.Equals(Categories.RecycleBin.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            _policy.RetentionDaysByCategory[category.Id] = retentionDays;
        }
    }

    private async Task SetFindingSelectionAsync(bool isSelected, string statusMessage)
    {
        BeginWork();
        var previousStatus = StatusMessage;
        _suppressSelectionSummary = true;
        try
        {
            StatusMessage = statusMessage;
            await Task.Yield();

            var index = 0;
            foreach (var item in FindingItems)
            {
                item.IsSelected = isSelected;
                index++;
                if (index % 200 == 0)
                {
                    await Task.Yield();
                }
            }
        }
        finally
        {
            _suppressSelectionSummary = false;
            UpdateSelectionSummary();
            CleanCommand.NotifyCanExecuteChanged();
            StatusMessage = previousStatus;
            EndWork();
        }
    }

    private CancellationToken BeginCancelableOperation(string cancelStatusMessage)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        _cancelStatusMessage = cancelStatusMessage;
        CancelCommand.NotifyCanExecuteChanged();
        return _operationCts.Token;
    }

    private void EndCancelableOperation()
    {
        _operationCts?.Dispose();
        _operationCts = null;
        _cancelStatusMessage = null;
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void BeginWork()
    {
        _activeOperations++;
        if (_activeOperations == 1)
        {
            IsWorking = true;
            EmptyRecycleBinCommand.NotifyCanExecuteChanged();
        }
    }

    private void EndWork()
    {
        if (_activeOperations > 0)
        {
            _activeOperations--;
        }

        if (_activeOperations == 0)
        {
            IsWorking = false;
            EmptyRecycleBinCommand.NotifyCanExecuteChanged();
        }
    }
}
