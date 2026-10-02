using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class SettingsPage : INavigableView<SettingsViewModel>, INavigationAware
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync() => ViewModel.OnNavigatedToAsync();

    public Task OnNavigatedFromAsync() => ViewModel.OnNavigatedFromAsync();
}
