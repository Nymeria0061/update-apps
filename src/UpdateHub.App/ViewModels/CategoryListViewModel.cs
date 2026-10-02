using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Core.Models;

namespace UpdateHub.App.ViewModels;

/// <summary>
/// Filtered, searchable projection of <see cref="UpdateCenterViewModel.Items"/> for one or more categories.
/// Used by the Applications, Windows/Drivers and Firmware pages.
/// </summary>
public abstract partial class CategoryListViewModel : ObservableObject
{
    private readonly HashSet<UpdateCategory> _categories;

    protected CategoryListViewModel(UpdateCenterViewModel center, params UpdateCategory[] categories)
    {
        Center = center;
        _categories = categories.ToHashSet();
        View = new ListCollectionView(center.Items) { Filter = Filter };
        View.SortDescriptions.Add(new SortDescription(nameof(UpdateItemViewModel.Category), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(UpdateItemViewModel.Name), ListSortDirection.Ascending));
        center.ItemsChanged += (_, _) => Refresh();
    }

    public UpdateCenterViewModel Center { get; }

    public ICollectionView View { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private int _visibleCount;

    partial void OnSearchTextChanged(string value) => Refresh();

    private bool Filter(object obj)
    {
        if (obj is not UpdateItemViewModel item || !_categories.Contains(item.Category))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || item.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || (item.Publisher?.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    public void Refresh()
    {
        View.Refresh();
        VisibleCount = View.Cast<object>().Count();
        IsEmpty = VisibleCount == 0;
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in View.Cast<UpdateItemViewModel>().Where(i => i.IsActionable && i.CanInstall))
        {
            item.IsSelected = true;
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in View.Cast<UpdateItemViewModel>())
        {
            item.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task InstallVisibleSelectedAsync()
    {
        var selected = View.Cast<UpdateItemViewModel>().Where(i => i.IsSelected && i.IsActionable && i.CanInstall).ToList();
        return Center.InstallWithConfirmationAsync(selected, CancellationToken.None);
    }

    [RelayCommand]
    private Task InstallAllVisibleAsync()
    {
        var all = View.Cast<UpdateItemViewModel>().Where(i => i.IsActionable && i.CanInstall).ToList();
        return Center.InstallWithConfirmationAsync(all, CancellationToken.None);
    }
}

public sealed class ApplicationsViewModel : CategoryListViewModel
{
    public ApplicationsViewModel(UpdateCenterViewModel center) : base(center, UpdateCategory.Application)
    {
    }
}

public sealed class WindowsUpdatesViewModel : CategoryListViewModel
{
    public WindowsUpdatesViewModel(UpdateCenterViewModel center) : base(center, UpdateCategory.WindowsUpdate, UpdateCategory.Driver)
    {
    }
}
