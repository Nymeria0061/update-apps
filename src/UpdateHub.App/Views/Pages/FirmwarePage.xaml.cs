using UpdateHub.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.Views.Pages;

public partial class FirmwarePage : INavigableView<FirmwareViewModel>, INavigationAware
{
    public FirmwarePage(FirmwareViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
    }

    public FirmwareViewModel ViewModel { get; }

    public Task OnNavigatedToAsync() => ViewModel.OnNavigatedToAsync();

    public Task OnNavigatedFromAsync() => ViewModel.OnNavigatedFromAsync();
}
