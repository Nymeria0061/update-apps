using System.ComponentModel;
using UpdateHub.App.ViewModels;
using UpdateHub.App.Views.Pages;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;

namespace UpdateHub.App.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow(
        UpdateCenterViewModel viewModel,
        INavigationViewPageProvider pageProvider,
        INavigationService navigationService,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService)
    {
        ViewModel = viewModel;
        DataContext = this;

        InitializeComponent();

        RootNavigation.SetPageProviderService(pageProvider);
        navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }

    public UpdateCenterViewModel ViewModel { get; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (ViewModel.IsInstalling)
        {
            var result = System.Windows.MessageBox.Show(
                "Bir kurulum devam ediyor. Şimdi kapatmak kurulumu yarıda bırakabilir. Yine de çıkılsın mı?",
                "UpdateHub",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }
}
