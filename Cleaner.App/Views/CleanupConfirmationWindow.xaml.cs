using System.Windows;
using Cleaner.App.ViewModels;

namespace Cleaner.App.Views;

public partial class CleanupConfirmationWindow : Window
{
    public CleanupConfirmationWindow(IReadOnlyList<FindingItemViewModel> items)
    {
        InitializeComponent();
        var viewModel = new CleanupConfirmationViewModel(items);
        viewModel.RequestClose += OnRequestClose;
        DataContext = viewModel;
    }

    public CleanupConfirmationViewModel.CleanupDecision Decision { get; private set; }
        = CleanupConfirmationViewModel.CleanupDecision.Cancel;

    private void OnRequestClose(CleanupConfirmationViewModel.CleanupDecision decision)
    {
        if (decision == CleanupConfirmationViewModel.CleanupDecision.Permanent)
        {
            var confirmation = MessageBox.Show(
                "Are you sure you want to permanently delete the selected files?",
                "Permanently Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Decision = decision;
        DialogResult = decision != CleanupConfirmationViewModel.CleanupDecision.Cancel;
        Close();
    }
}
