using Cleaner.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

public sealed partial class CategoryItemViewModel : ObservableObject
{
    public Category Category { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string Id => Category.Id;
    public string Name => Category.Name;

    public CategoryItemViewModel(Category category)
    {
        Category = category;
        _isSelected = category.DefaultEnabled;
    }
}
