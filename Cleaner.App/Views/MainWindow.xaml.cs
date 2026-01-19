using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class MainWindow : Window
{
    private const string PathColumnHeader = "Path";
    private INotifyCollectionChanged? _findingItems;
    private bool _pendingColumnResize;
    private double? _pathColumnWidth;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void FindingGrid_OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachFindingItems();
        QueueColumnResize();
    }

    private void FindingGrid_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachFindingItems();
        QueueColumnResize();
    }

    private void AllFindingsHeaderCheckBox_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ToggleAllFindingsSelection();
        e.Handled = true;
    }

    private void AllFindingsHeaderCheckBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }

        ToggleAllFindingsSelection();
        e.Handled = true;
    }

    private void ToggleAllFindingsSelection()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.FindingItems.Count == 0)
        {
            return;
        }

        viewModel.AllFindingsSelected = viewModel.AllFindingsSelected != true;
    }

    private void AttachFindingItems()
    {
        if (FindingGrid.ItemsSource is INotifyCollectionChanged items)
        {
            if (_findingItems != null)
            {
                _findingItems.CollectionChanged -= OnFindingItemsChanged;
            }

            _findingItems = items;
            _findingItems.CollectionChanged += OnFindingItemsChanged;
        }
    }

    private void OnFindingItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        QueueColumnResize();
    }

    private void QueueColumnResize()
    {
        if (_pendingColumnResize)
        {
            return;
        }

        _pendingColumnResize = true;
        Dispatcher.BeginInvoke(() =>
        {
            _pendingColumnResize = false;
            AdjustColumnWidths();
        }, DispatcherPriority.Background);
    }

    private void AdjustColumnWidths()
    {
        if (FindingGrid.Columns.Count == 0)
        {
            return;
        }

        FindingGrid.UpdateLayout();
        CachePathColumnWidth();

        if (FindingGrid.Items.Count == 0)
        {
            ApplyUniformHeaderWidths();
            return;
        }

        foreach (var column in FindingGrid.Columns)
        {
            if (column.Header is not string header
                || string.IsNullOrWhiteSpace(header)
                || header.Equals(PathColumnHeader, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            column.Width = DataGridLength.SizeToHeader;
            FindingGrid.UpdateLayout();
            var headerWidth = column.ActualWidth;

            column.Width = DataGridLength.SizeToCells;
            FindingGrid.UpdateLayout();
            var cellWidth = column.ActualWidth;

            column.Width = new DataGridLength(Math.Max(headerWidth, cellWidth));
        }

        ApplyPathColumnWidth();
    }

    private void CachePathColumnWidth()
    {
        if (_pathColumnWidth.HasValue)
        {
            return;
        }

        var pathColumn = FindColumn(PathColumnHeader);
        if (pathColumn != null && pathColumn.Width.IsAbsolute)
        {
            _pathColumnWidth = pathColumn.Width.Value;
        }
        else
        {
            _pathColumnWidth = 420;
        }
    }

    private void ApplyPathColumnWidth()
    {
        if (!_pathColumnWidth.HasValue)
        {
            return;
        }

        var pathColumn = FindColumn(PathColumnHeader);
        if (pathColumn != null)
        {
            pathColumn.Width = new DataGridLength(_pathColumnWidth.Value);
        }
    }

    private void ApplyUniformHeaderWidths()
    {
        var maxHeaderWidth = GetMaxHeaderWidth();
        if (maxHeaderWidth <= 0)
        {
            return;
        }

        foreach (var column in FindingGrid.Columns)
        {
            if (column.Header is not string header || string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            column.Width = new DataGridLength(maxHeaderWidth);
        }
    }

    private double GetMaxHeaderWidth()
    {
        double maxWidth = 0;

        foreach (var column in FindingGrid.Columns)
        {
            if (column.Header is not string header || string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            column.Width = DataGridLength.SizeToHeader;
            FindingGrid.UpdateLayout();
            maxWidth = Math.Max(maxWidth, column.ActualWidth);
        }

        return maxWidth;
    }

    private DataGridColumn? FindColumn(string headerText)
    {
        foreach (var column in FindingGrid.Columns)
        {
            if (column.Header is string header
                && header.Equals(headerText, StringComparison.OrdinalIgnoreCase))
            {
                return column;
            }
        }

        return null;
    }
}
