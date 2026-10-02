using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class DashboardPage : INavigableView<DashboardViewModel>, INavigationAware
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public DashboardViewModel ViewModel { get; }

    public Task OnNavigatedToAsync() => ViewModel.OnNavigatedToAsync();

    public Task OnNavigatedFromAsync() => ViewModel.OnNavigatedFromAsync();
}
