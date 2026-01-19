using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

public sealed partial class DriveItemViewModel : ObservableObject
{
    public string Root { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _totalDisplay = "Not scanned";

    public DriveItemViewModel(string root, bool isSelected)
    {
        Root = root;
        _isSelected = isSelected;
    }
}
