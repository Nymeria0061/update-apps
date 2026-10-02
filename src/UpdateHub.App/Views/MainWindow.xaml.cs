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
        FitToWorkArea();

        RootNavigation.SetPageProviderService(pageProvider);
        navigationService.SetNavigationControl(RootNavigation);
        snackbarService.SetSnackbarPresenter(SnackbarPresenter);
        contentDialogService.SetDialogHost(RootContentDialog);

        Loaded += (_, _) => RootNavigation.Navigate(typeof(DashboardPage));
    }

    public UpdateCenterViewModel ViewModel { get; }

    /// <summary>Default size must never exceed the monitor's work area (the bottom status bar was getting cut off on 768p screens).</summary>
    private void FitToWorkArea()
    {
        var area = System.Windows.SystemParameters.WorkArea;
        if (Height > area.Height - 16)
        {
            Height = Math.Max(MinHeight, area.Height - 16);
        }

        if (Width > area.Width - 16)
        {
            Width = Math.Max(MinWidth, area.Width - 16);
        }
    }

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
