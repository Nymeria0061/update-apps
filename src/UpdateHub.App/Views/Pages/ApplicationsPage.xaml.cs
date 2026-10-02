using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class ApplicationsPage : INavigableView<ApplicationsViewModel>, INavigationAware
{
    public ApplicationsPage(ApplicationsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public ApplicationsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        ViewModel.Refresh();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
