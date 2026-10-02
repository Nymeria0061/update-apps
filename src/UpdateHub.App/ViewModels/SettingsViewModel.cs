using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.App.Services;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Controls;

namespace UpdateHub.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ISettingsService _settings;
    private readonly AppThemeService _theme;
    private readonly ISnackbarService _snackbar;
    private bool _loading;

    public SettingsViewModel(ISettingsService settings, AppThemeService theme, ISnackbarService snackbar)
    {
        _settings = settings;
        _theme = theme;
        _snackbar = snackbar;
        Load();
    }

    public string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public ObservableCollection<string> ExcludedIds { get; } = new();

    public ObservableCollection<string> WingetManualIds { get; } = new();

    public ObservableCollection<SkippedVersionEntry> SkippedVersions { get; } = new();

    [ObservableProperty] private bool _includeUnknownVersionsInBulk;

    [ObservableProperty] private bool _stableOnly;
    [ObservableProperty] private bool _scanOnStartup;
    [ObservableProperty] private bool _includeApplications;
    [ObservableProperty] private bool _includeWindowsUpdates;
    [ObservableProperty] private bool _includeDrivers;
    [ObservableProperty] private bool _includeFirmware;
    [ObservableProperty] private bool _allowFirmwareInBulkUpdate;
    [ObservableProperty] private bool _allowCustomWingetSources;
    [ObservableProperty] private int _themeIndex;
    [ObservableProperty] private string? _hpImageAssistantPath;
    [ObservableProperty] private string? _wingetPath;
    [ObservableProperty] private string? _selectedExcludedId;

    private void Load()
    {
        _loading = true;
        var s = _settings.Current;
        StableOnly = s.StableOnly;
        ScanOnStartup = s.ScanOnStartup;
        IncludeApplications = s.IncludeApplications;
        IncludeWindowsUpdates = s.IncludeWindowsUpdates;
        IncludeDrivers = s.IncludeDrivers;
        IncludeFirmware = s.IncludeFirmware;
        AllowFirmwareInBulkUpdate = s.AllowFirmwareInBulkUpdate;
        AllowCustomWingetSources = s.AllowedWingetSources.Count == 0;
        ThemeIndex = (int)s.Theme;
        HpImageAssistantPath = s.HpImageAssistantPath;
        WingetPath = s.WingetPath;
        IncludeUnknownVersionsInBulk = s.IncludeUnknownVersionsInBulk;
        ExcludedIds.Clear();
        foreach (var id in s.ExcludedIds)
        {
            ExcludedIds.Add(id);
        }

        WingetManualIds.Clear();
        foreach (var id in s.WingetManualIds)
        {
            WingetManualIds.Add(id);
        }

        SkippedVersions.Clear();
        foreach (var kv in s.SkippedVersions.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            SkippedVersions.Add(new SkippedVersionEntry(kv.Key, kv.Value));
        }

        _loading = false;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(SelectedExcludedId))
        {
            return;
        }

        _ = SaveAsync();
    }

    private async Task SaveAsync()
    {
        var s = _settings.Current;
        s.StableOnly = StableOnly;
        s.ScanOnStartup = ScanOnStartup;
        s.IncludeApplications = IncludeApplications;
        s.IncludeWindowsUpdates = IncludeWindowsUpdates;
        s.IncludeDrivers = IncludeDrivers;
        s.IncludeFirmware = IncludeFirmware;
        s.AllowFirmwareInBulkUpdate = AllowFirmwareInBulkUpdate;
        s.AllowedWingetSources = AllowCustomWingetSources ? [] : ["winget", "msstore"];
        s.Theme = (AppTheme)ThemeIndex;
        s.HpImageAssistantPath = string.IsNullOrWhiteSpace(HpImageAssistantPath) ? null : HpImageAssistantPath.Trim();
        s.WingetPath = string.IsNullOrWhiteSpace(WingetPath) ? null : WingetPath.Trim();
        s.ExcludedIds = ExcludedIds.ToList();
        s.WingetManualIds = WingetManualIds.ToList();
        s.SkippedVersions = SkippedVersions.ToDictionary(e => e.Id, e => e.Version);
        s.IncludeUnknownVersionsInBulk = IncludeUnknownVersionsInBulk;
        await _settings.SaveAsync();
        _theme.Apply(s.Theme);
    }

    [RelayCommand]
    private async Task RemoveExcludedAsync(string? id)
    {
        if (id is null)
        {
            return;
        }

        ExcludedIds.Remove(id);
        await SaveAsync();
        _snackbar.Show("Geri alındı", $"{id} bir sonraki taramada yeniden listelenecek.", ControlAppearance.Secondary, new SymbolIcon(SymbolRegular.Eye24), TimeSpan.FromSeconds(4));
    }

    [RelayCommand]
    private async Task RemoveSkippedAsync(SkippedVersionEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        SkippedVersions.Remove(entry);
        await SaveAsync();
        _snackbar.Show("Geri alındı", $"{entry.Id} {entry.Version} bir sonraki taramada yeniden listelenecek.", ControlAppearance.Secondary, new SymbolIcon(SymbolRegular.ArrowReset24), TimeSpan.FromSeconds(4));
    }

    [RelayCommand]
    private async Task RemoveManualAsync(string? id)
    {
        if (id is null)
        {
            return;
        }

        WingetManualIds.Remove(id);
        await SaveAsync();
        _snackbar.Show("Sıfırlandı", $"{id} bir sonraki taramada yeniden winget ile denenecek.", ControlAppearance.Secondary, new SymbolIcon(SymbolRegular.ArrowReset24), TimeSpan.FromSeconds(4));
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Core.Services.AppPaths.DataDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", Core.Services.AppPaths.DataDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    public Task OnNavigatedToAsync()
    {
        Load();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}

public sealed record SkippedVersionEntry(string Id, string Version)
{
    public string Label => $"{Id} — {Version}";
}
