using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using UpdateHub.Core.Services;
using UpdateHub.Windows.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace UpdateHub.App.ViewModels;

public sealed partial class ProviderStatusViewModel : ObservableObject
{
    public ProviderStatusViewModel(IUpdateProvider provider)
    {
        Provider = provider;
        Name = provider.DisplayName;
    }

    public IUpdateProvider Provider { get; }

    public string Name { get; }

    [ObservableProperty]
    private string _status = "Henüz taranmadı";

    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isSkipped;
}

/// <summary>
/// Single source of truth for the whole app: the last scan, the running operation and its progress.
/// Pages are thin projections over <see cref="Items"/>.
/// </summary>
public sealed partial class UpdateCenterViewModel : ObservableObject
{
    private readonly UpdateOrchestrator _orchestrator;
    private readonly ISettingsService _settings;
    private readonly ISystemInfoProvider _systemInfo;
    private readonly IRebootService _reboot;
    private readonly ISnackbarService _snackbar;
    private readonly IContentDialogService _dialogs;
    private readonly ILogger<UpdateCenterViewModel> _logger;

    public UpdateCenterViewModel(
        UpdateOrchestrator orchestrator,
        ISettingsService settings,
        ISystemInfoProvider systemInfo,
        IRebootService reboot,
        ISnackbarService snackbar,
        IContentDialogService dialogs,
        ILogger<UpdateCenterViewModel> logger)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        _systemInfo = systemInfo;
        _reboot = reboot;
        _snackbar = snackbar;
        _dialogs = dialogs;
        _logger = logger;

        foreach (var provider in orchestrator.Providers)
        {
            Providers.Add(new ProviderStatusViewModel(provider));
        }
    }

    public ObservableCollection<UpdateItemViewModel> Items { get; } = new();

    public ObservableCollection<ProviderStatusViewModel> Providers { get; } = new();

    public event EventHandler? ItemsChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallSelectedCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private string _statusMessage = "Güncellemeleri görmek için taramayı başlatın.";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private bool _rebootRequired;

    [ObservableProperty]
    private DateTimeOffset? _lastScan;

    [ObservableProperty]
    private bool _hasScanned;

    [ObservableProperty]
    private SystemInfo? _system;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdates))]
    [NotifyPropertyChangedFor(nameof(IsAllUpToDate))]
    [NotifyCanExecuteChangedFor(nameof(InstallAllCommand))]
    private int _totalCount;

    [ObservableProperty]
    private int _appCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowsAndDriverCount))]
    private int _windowsCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowsAndDriverCount))]
    private int _driverCount;

    public int WindowsAndDriverCount => WindowsCount + DriverCount;

    [ObservableProperty]
    private int _firmwareCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallAllCommand))]
    private int _bulkCount;

    [ObservableProperty]
    private int _preReleaseCount;

    public bool IsBusy => IsScanning || IsInstalling;

    public bool IsIdle => !IsBusy;

    public bool HasUpdates => TotalCount > 0;

    public bool IsAllUpToDate => HasScanned && TotalCount == 0;

    public string LastScanLabel => LastScan is null ? "Henüz taranmadı" : $"Son tarama: {LastScan:HH:mm}";

    partial void OnLastScanChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastScanLabel));

    public async Task InitializeAsync()
    {
        try
        {
            System = await _systemInfo.GetSystemInfoAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System information could not be read");
        }

        if (_settings.Current.ScanOnStartup && ScanCommand.CanExecute(null))
        {
            await ScanCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(IsIdle))]
    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        IsScanning = true;
        IsProgressIndeterminate = true;
        StatusMessage = "Güncelleme kaynakları taranıyor…";
        RebootRequired = false;
        foreach (var p in Providers)
        {
            p.Status = "Taranıyor…";
            p.Count = 0;
            p.HasError = false;
            p.IsSkipped = false;
        }

        var progress = new Progress<ScanProgress>(p => StatusMessage = p.Message);
        try
        {
            var summary = await _orchestrator.ScanAsync(progress, cancellationToken);
            ApplyScan(summary);
            LastScan = summary.CompletedAt;
            HasScanned = true;
            StatusMessage = TotalCount == 0
                ? "Her şey güncel."
                : $"{TotalCount} güncelleme bulundu ({BulkCount} tanesi otomatik kurulabilir).";
            _logger.LogInformation("Scan finished: {Count} updates", TotalCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scan failed");
            StatusMessage = "Tarama sırasında hata oluştu: " + ex.Message;
            _snackbar.Show("Tarama başarısız", ex.Message, ControlAppearance.Danger, new SymbolIcon(SymbolRegular.ErrorCircle24), TimeSpan.FromSeconds(8));
        }
        finally
        {
            IsScanning = false;
            IsProgressIndeterminate = false;
        }
    }

    private void ApplyScan(ScanSummary summary)
    {
        Items.Clear();
        foreach (var result in summary.Results)
        {
            var status = Providers.First(p => p.Provider == result.Provider);
            if (result.Skipped)
            {
                status.Status = result.SkipReason ?? "Atlandı";
                status.IsSkipped = true;
            }
            else if (result.Error is not null)
            {
                status.Status = "Hata: " + result.Error.Message;
                status.HasError = true;
                _logger.LogError(result.Error, "{Provider} scan failed", result.Provider.DisplayName);
            }
            else
            {
                status.Status = result.Items.Count == 0 ? "Güncel" : $"{result.Items.Count} güncelleme";
                status.Count = result.Items.Count;
            }

            foreach (var item in result.Items)
            {
                Items.Add(new UpdateItemViewModel(item, InstallSingleAsync, ExcludeAsync));
            }
        }

        RecalculateCounts();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RecalculateCounts()
    {
        var pending = Items.Where(i => !i.IsDone).ToList();
        TotalCount = pending.Count;
        AppCount = pending.Count(i => i.Category == UpdateCategory.Application);
        WindowsCount = pending.Count(i => i.Category == UpdateCategory.WindowsUpdate);
        DriverCount = pending.Count(i => i.Category == UpdateCategory.Driver);
        FirmwareCount = pending.Count(i => i.Category == UpdateCategory.Firmware);
        PreReleaseCount = pending.Count(i => i.IsPreRelease);
        BulkCount = _orchestrator.SelectForBulkInstall(pending.Select(i => i.Item)).Count;
    }

    private bool CanInstallAll() => IsIdle && BulkCount > 0;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanInstallAll))]
    private async Task InstallAllAsync(CancellationToken cancellationToken)
    {
        var selected = _orchestrator.SelectForBulkInstall(Items.Where(i => i.IsActionable).Select(i => i.Item))
            .Select(item => Items.First(vm => vm.Item == item))
            .ToList();
        await InstallItemsAsync(selected, cancellationToken);
    }

    private bool CanInstallSelected() => IsIdle;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanInstallSelected))]
    private async Task InstallSelectedAsync(CancellationToken cancellationToken)
    {
        var selected = Items.Where(i => i.IsSelected && i.IsActionable && i.CanInstall).ToList();
        await InstallWithConfirmationAsync(selected, cancellationToken);
    }

    /// <summary>Entry point for pages: validates the selection, asks about risky items, then installs.</summary>
    public async Task InstallWithConfirmationAsync(IReadOnlyList<UpdateItemViewModel> items, CancellationToken cancellationToken)
    {
        if (!IsIdle)
        {
            _snackbar.Show("Meşgul", "Devam eden işlem bitene kadar bekleyin.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Info24), TimeSpan.FromSeconds(4));
            return;
        }

        if (items.Count == 0)
        {
            _snackbar.Show("Seçim yok", "Önce listeden en az bir güncelleme seçin.", ControlAppearance.Caution, new SymbolIcon(SymbolRegular.Info24), TimeSpan.FromSeconds(4));
            return;
        }

        if (!await ConfirmRiskyItemsAsync(items))
        {
            return;
        }

        await InstallItemsAsync(items, cancellationToken);
    }

    private Task InstallSingleAsync(UpdateItemViewModel item) => InstallWithConfirmationAsync([item], CancellationToken.None);

    /// <summary>Pre-release builds and firmware are never installed without an explicit confirmation.</summary>
    private async Task<bool> ConfirmRiskyItemsAsync(IReadOnlyList<UpdateItemViewModel> items)
    {
        var pre = items.Where(i => i.IsPreRelease).ToList();
        var firmware = items.Where(i => i.IsFirmware).ToList();
        if (pre.Count == 0 && firmware.Count == 0)
        {
            return true;
        }

        var lines = new List<string>();
        if (pre.Count > 0)
        {
            lines.Add($"{pre.Count} öğe ön sürüm (beta/preview) kanalından:\n• " + string.Join("\n• ", pre.Take(5).Select(i => i.Name)));
        }

        if (firmware.Count > 0)
        {
            lines.Add("BIOS/UEFI güncellemesi yapılacak. İşlem sırasında bilgisayarı kapatmayın ve dizüstü bilgisayarı güç adaptörüne takılı tutun:\n• " + string.Join("\n• ", firmware.Select(i => i.Name)));
        }

        var result = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Devam edilsin mi?",
            Content = string.Join("\n\n", lines),
            PrimaryButtonText = "Evet, kur",
            CloseButtonText = "Vazgeç",
        }, CancellationToken.None);

        return result == ContentDialogResult.Primary;
    }

    public async Task InstallItemsAsync(IReadOnlyList<UpdateItemViewModel> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return;
        }

        IsInstalling = true;
        IsProgressIndeterminate = false;
        ProgressPercent = 0;
        foreach (var item in items)
        {
            item.State = ItemState.Queued;
            item.StatusMessage = null;
        }

        var progress = new Progress<BatchProgress>(p =>
        {
            var basePercent = p.Total == 0 ? 100 : p.Completed * 100.0 / p.Total;
            var itemShare = p.Total == 0 ? 0 : (p.ItemPercent ?? 0) / p.Total;
            ProgressPercent = Math.Min(100, basePercent + itemShare);
            StatusMessage = p.CurrentItem is null ? p.Message : $"{p.Completed + 1}/{p.Total} · {p.CurrentItem.Name}: {p.Message}";

            if (p.CurrentItem is not null)
            {
                var vm = items.FirstOrDefault(i => i.Item == p.CurrentItem);
                if (vm is not null)
                {
                    vm.State = ItemState.Installing;
                    vm.StatusMessage = p.Message;
                    vm.Progress = p.ItemPercent;
                }
            }
        });

        try
        {
            var summary = await _orchestrator.InstallAsync(
                items.Select(i => i.Item).ToList(),
                progress,
                (item, result) =>
                {
                    var vm = items.First(i => i.Item == item);
                    Application.Current.Dispatcher.Invoke(() => vm.Apply(result));
                    if (result.IsSuccess)
                    {
                        _logger.LogInformation("Installed {Name}: {Outcome}", item.Name, result.Outcome);
                    }
                    else
                    {
                        _logger.LogWarning("Install of {Name} ended with {Outcome}: {Message}", item.Name, result.Outcome, result.Message);
                    }

                    return Task.CompletedTask;
                },
                cancellationToken);

            RebootRequired |= summary.RebootRequired;
            var message = $"{summary.Succeeded} başarılı, {summary.Failed} başarısız" + (summary.Manual > 0 ? $", {summary.Manual} elle kurulum" : string.Empty);
            StatusMessage = "Kurulum tamamlandı: " + message + (RebootRequired ? " · Yeniden başlatma gerekiyor." : string.Empty);
            _snackbar.Show(
                "Kurulum tamamlandı",
                message,
                summary.Failed == 0 ? ControlAppearance.Success : ControlAppearance.Caution,
                new SymbolIcon(summary.Failed == 0 ? SymbolRegular.CheckmarkCircle24 : SymbolRegular.Warning24),
                TimeSpan.FromSeconds(6));
        }
        catch (OperationCanceledException)
        {
            foreach (var item in items.Where(i => i.IsBusy))
            {
                item.State = ItemState.Cancelled;
            }

            StatusMessage = "Kurulum iptal edildi.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch install failed");
            StatusMessage = "Kurulum sırasında hata: " + ex.Message;
        }
        finally
        {
            foreach (var item in items.Where(i => i.State == ItemState.Queued))
            {
                item.State = ItemState.Pending;
            }

            IsInstalling = false;
            ProgressPercent = 0;
            RecalculateCounts();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ExcludeAsync(UpdateItemViewModel item)
    {
        var settings = _settings.Current;
        if (!settings.ExcludedIds.Contains(item.Id, StringComparer.OrdinalIgnoreCase))
        {
            settings.ExcludedIds.Add(item.Id);
            await _settings.SaveAsync();
        }

        Items.Remove(item);
        RecalculateCounts();
        ItemsChanged?.Invoke(this, EventArgs.Empty);
        _snackbar.Show("Yoksayıldı", $"{item.Name} artık listelenmeyecek. Ayarlar'dan geri alabilirsiniz.", ControlAppearance.Secondary, new SymbolIcon(SymbolRegular.Eye24), TimeSpan.FromSeconds(5));
    }

    [RelayCommand]
    private async Task RestartNowAsync()
    {
        var result = await _dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Yeniden başlatılsın mı?",
            Content = "Açık çalışmalarınızı kaydedin. Bilgisayar 30 saniye içinde yeniden başlatılacak.",
            PrimaryButtonText = "Yeniden başlat",
            CloseButtonText = "Daha sonra",
        }, CancellationToken.None);

        if (result == ContentDialogResult.Primary)
        {
            await _reboot.RestartAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        }
    }

    [RelayCommand]
    private void SelectAll(bool select)
    {
        foreach (var item in Items.Where(i => i.IsActionable && i.CanInstall))
        {
            item.IsSelected = select;
        }
    }
}
