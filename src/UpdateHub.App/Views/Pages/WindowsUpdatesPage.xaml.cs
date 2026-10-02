using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class WindowsUpdatesPage : INavigableView<WindowsUpdatesViewModel>, INavigationAware
{
    public WindowsUpdatesPage(WindowsUpdatesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public WindowsUpdatesViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        ViewModel.Refresh();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
