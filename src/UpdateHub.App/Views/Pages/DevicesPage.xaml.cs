using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class DevicesPage : INavigableView<DevicesViewModel>, INavigationAware
{
    public DevicesPage(DevicesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public DevicesViewModel ViewModel { get; }

    public Task OnNavigatedToAsync() => ViewModel.OnNavigatedToAsync();

    public Task OnNavigatedFromAsync() => ViewModel.OnNavigatedFromAsync();
}
