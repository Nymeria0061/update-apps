using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UpdateHub.App.Services;
using UpdateHub.App.ViewModels;
using UpdateHub.App.Views;
using UpdateHub.App.Views.Pages;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Services;
using UpdateHub.Windows.Firmware;
using UpdateHub.Windows.Services;
using UpdateHub.Windows.WindowsUpdate;
using UpdateHub.Windows.Winget;
using UpdateHub.Windows.Wmi;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.DependencyInjection;

namespace UpdateHub.App;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    private static readonly IHost AppHost = Host.CreateDefaultBuilder()
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddDebug();
        })
        .ConfigureServices(services =>
        {
            // Infrastructure
            services.AddSingleton<LogStore>();
            services.AddSingleton<ILoggerProvider, LogStoreLoggerProvider>();
            services.AddSingleton<ISettingsService>(_ => new JsonSettingsService());
            services.AddSingleton<IProcessRunner, ProcessRunner>();
            services.AddSingleton<ISystemInfoProvider, WmiSystemInfoProvider>();
            services.AddSingleton<IDeviceInventory, WmiDeviceInventory>();
            services.AddSingleton<IRebootService, RebootService>();
            services.AddSingleton<AppThemeService>();

            // Update providers (order = display order)
            services.AddSingleton<WingetLocator>();
            services.AddSingleton<WingetProvider>();
            services.AddSingleton<IUpdateProvider>(sp => sp.GetRequiredService<WingetProvider>());
            services.AddSingleton<IPackageActionProvider, WingetPackageActions>();
            services.AddSingleton<WindowsUpdateAgent>();
            services.AddSingleton<IUpdateProvider, WindowsSoftwareUpdateProvider>();
            services.AddSingleton<IUpdateProvider, WindowsDriverUpdateProvider>();
            services.AddSingleton<IUpdateProvider, WindowsFirmwareUpdateProvider>();
            services.AddSingleton<IOemFirmwareStrategy, DellCommandUpdateStrategy>();
            services.AddSingleton<IOemFirmwareStrategy, LenovoSystemUpdateStrategy>();
            services.AddSingleton<IOemFirmwareStrategy, HpImageAssistantStrategy>();
            services.AddSingleton<OemFirmwareProvider>();
            services.AddSingleton<IUpdateProvider>(sp => sp.GetRequiredService<OemFirmwareProvider>());
            services.AddSingleton<UpdateOrchestrator>();

            // WPF-UI services
            services.AddNavigationViewPageProvider();
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<ISnackbarService, SnackbarService>();
            services.AddSingleton<IContentDialogService, ContentDialogService>();

            // Shell + pages
            services.AddSingleton<UpdateCenterViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<DashboardPage>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<ApplicationsPage>();
            services.AddSingleton<ApplicationsViewModel>();
            services.AddSingleton<WindowsUpdatesPage>();
            services.AddSingleton<WindowsUpdatesViewModel>();
            services.AddSingleton<FirmwarePage>();
            services.AddSingleton<FirmwareViewModel>();
            services.AddSingleton<DevicesPage>();
            services.AddSingleton<DevicesViewModel>();
            services.AddSingleton<LogPage>();
            services.AddSingleton<LogViewModel>();
            services.AddSingleton<SettingsPage>();
            services.AddSingleton<SettingsViewModel>();
        })
        .Build();

    public static T GetService<T>() where T : class =>
        AppHost.Services.GetService<T>() ?? throw new InvalidOperationException($"{typeof(T).Name} is not registered.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, @"Local\UpdateHub.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        await AppHost.StartAsync();

        var settings = GetService<ISettingsService>();
        await settings.LoadAsync();
        GetService<LogStore>().MaxEntries = settings.Current.MaxLogEntries;
        GetService<ILogger<App>>().LogInformation("UpdateHub starting on {OS}", Environment.OSVersion.VersionString);

        var window = GetService<MainWindow>();
        var theme = GetService<AppThemeService>();
        theme.Attach(window);
        theme.Apply(settings.Current.Theme);
        window.Show();

        await GetService<UpdateCenterViewModel>().InitializeAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await AppHost.StopAsync();
        AppHost.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            GetService<ILogger<App>>().LogCritical(e.Exception, "Unhandled UI exception");
        }
        catch
        {
            // Logging must never mask the original failure.
        }

        MessageBox.Show(e.Exception.Message, "UpdateHub – beklenmeyen hata", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
