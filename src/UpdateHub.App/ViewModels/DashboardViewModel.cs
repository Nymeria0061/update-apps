using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Core.Models;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, INavigationAware
{
    private readonly INavigationService _navigation;

    public DashboardViewModel(UpdateCenterViewModel center, INavigationService navigation)
    {
        Center = center;
        _navigation = navigation;
        center.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateCenterViewModel.System))
            {
                OnPropertyChanged(string.Empty);
            }
        };
    }

    public UpdateCenterViewModel Center { get; }

    public SystemInfo? System => Center.System;

    public string DeviceTitle => System is null
        ? "Sistem bilgisi okunuyor…"
        : string.IsNullOrWhiteSpace(System.Model) ? System.Manufacturer : $"{System.Manufacturer} {System.Model}".Trim();

    public string OsLine => System is null ? string.Empty : $"{System.OsName} · {System.OsDisplayVersion} · {System.Architecture}";

    public string BiosLine => System is null
        ? string.Empty
        : $"{System.BiosVendor} {System.BiosVersion}".Trim() + (System.BiosReleaseDate is null ? string.Empty : $" · {System.BiosReleaseDate:dd.MM.yyyy}");

    public string FirmwareLine => System is null
        ? string.Empty
        : (System.FirmwareType switch
        {
            FirmwareType.Uefi => "UEFI",
            FirmwareType.LegacyBios => "Legacy BIOS",
            _ => "Bilinmiyor",
        }) + (System.SecureBootEnabled switch
        {
            true => " · Secure Boot açık",
            false => " · Secure Boot kapalı",
            _ => string.Empty,
        });

    public string CpuLine => System?.Cpu ?? string.Empty;

    public string MemoryLine => System is null || System.TotalMemoryBytes == 0 ? string.Empty : $"{System.TotalMemoryBytes / 1024.0 / 1024 / 1024:0.#} GB RAM";

    public string GpuLine => System is null ? string.Empty : string.Join(" · ", System.Gpus);

    public bool IsElevated => System?.IsElevated ?? false;

    public bool IsNotElevated => System is not null && !System.IsElevated;

    [RelayCommand]
    private void GoToApplications() => _navigation.Navigate(typeof(Views.Pages.ApplicationsPage));

    [RelayCommand]
    private void GoToWindows() => _navigation.Navigate(typeof(Views.Pages.WindowsUpdatesPage));

    [RelayCommand]
    private void GoToFirmware() => _navigation.Navigate(typeof(Views.Pages.FirmwarePage));

    [RelayCommand]
    private void GoToDevices() => _navigation.Navigate(typeof(Views.Pages.DevicesPage));

    public Task OnNavigatedToAsync()
    {
        OnPropertyChanged(string.Empty);
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
