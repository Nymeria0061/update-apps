using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Models;
using UpdateHub.Windows.Firmware;
using UpdateHub.Windows.Winget;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace UpdateHub.App.ViewModels;

public sealed partial class FirmwareViewModel : CategoryListViewModel, INavigationAware
{
    private readonly OemFirmwareProvider _oem;
    private readonly WingetProvider _winget;
    private readonly ISnackbarService _snackbar;
    private readonly ILogger<FirmwareViewModel> _logger;

    public FirmwareViewModel(UpdateCenterViewModel center, OemFirmwareProvider oem, WingetProvider winget, ISnackbarService snackbar, ILogger<FirmwareViewModel> logger)
        : base(center, UpdateCategory.Firmware)
    {
        _oem = oem;
        _winget = winget;
        _snackbar = snackbar;
        _logger = logger;
        center.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateCenterViewModel.System))
            {
                OnPropertyChanged(nameof(System));
                OnPropertyChanged(nameof(BiosVersion));
                OnPropertyChanged(nameof(BiosDate));
                OnPropertyChanged(nameof(BoardLine));
            }
        };
    }

    public SystemInfo? System => Center.System;

    public string BiosVersion => System is null ? "…" : $"{System.BiosVendor} {System.BiosVersion}".Trim();

    public string BiosDate => System?.BiosReleaseDate?.ToString("dd MMMM yyyy") ?? string.Empty;

    public string BoardLine => System is null ? string.Empty : $"{System.BaseBoardManufacturer} {System.BaseBoardProduct}".Trim();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolStatusLabel))]
    [NotifyPropertyChangedFor(nameof(CanInstallTool))]
    [NotifyPropertyChangedFor(nameof(HasToolDownloadUrl))]
    private FirmwareGuidance? _guidance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallTool))]
    private bool _isInstallingTool;

    public string ToolStatusLabel => Guidance switch
    {
        null => "Üretici bilgisi okunuyor…",
        { SupportsAutomation: false } => "Bu üretici için komut satırından çalışan resmi bir BIOS aracı yok.",
        { ToolInstalled: true } => $"{Guidance.ToolName} kurulu — BIOS taraması bu araçla yapılır.",
        _ => $"{Guidance.ToolName} kurulu değil. Kurduğunuzda BIOS güncellemeleri otomatik taranır.",
    };

    public bool CanInstallTool => Guidance is { SupportsAutomation: true, ToolInstalled: false, ToolWingetId: not null } && !IsInstallingTool;

    public bool HasToolDownloadUrl => Guidance?.ToolDownloadUrl is not null;

    public async Task OnNavigatedToAsync()
    {
        await RefreshGuidanceAsync();
        Refresh();
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private async Task RefreshGuidanceAsync()
    {
        try
        {
            Guidance = await _oem.GetGuidanceAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firmware guidance failed");
        }
    }

    [RelayCommand]
    private void OpenSupportPage()
    {
        if (Guidance?.SupportUrl is { } url)
        {
            OpenUrl(url);
        }
    }

    [RelayCommand]
    private void OpenToolDownloadPage()
    {
        if (Guidance?.ToolDownloadUrl is { } url)
        {
            OpenUrl(url);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallTool))]
    private async Task InstallToolAsync()
    {
        if (Guidance?.ToolWingetId is not { } id)
        {
            return;
        }

        IsInstallingTool = true;
        try
        {
            var result = await _winget.InstallPackageAsync(id, null, CancellationToken.None);
            if (result.IsSuccess)
            {
                _snackbar.Show("Araç kuruldu", $"{Guidance.ToolName} winget üzerinden kuruldu. Şimdi yeniden tarayabilirsiniz.", ControlAppearance.Success, new SymbolIcon(SymbolRegular.CheckmarkCircle24), TimeSpan.FromSeconds(6));
            }
            else
            {
                _snackbar.Show("Kurulamadı", result.Message ?? "winget kurulumu başarısız.", ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(8));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool install failed");
            _snackbar.Show("Kurulamadı", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(8));
        }
        finally
        {
            IsInstallingTool = false;
            await RefreshGuidanceAsync();
            InstallToolCommand.NotifyCanExecuteChanged();
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
}
