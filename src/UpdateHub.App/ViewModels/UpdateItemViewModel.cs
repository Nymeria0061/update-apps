using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;

namespace UpdateHub.App.ViewModels;

public enum ItemState
{
    Pending,
    Queued,
    Installing,
    Installed,
    Warning,
    RebootRequired,
    UpToDate,
    Failed,
    Manual,
    Cancelled,
}

/// <summary>What a row can ask the update center to do on its behalf.</summary>
public interface IUpdateItemActions
{
    Task InstallAsync(UpdateItemViewModel item);

    Task ExcludeAsync(UpdateItemViewModel item);

    Task SkipVersionAsync(UpdateItemViewModel item);

    Task<PackageLinks?> GetLinksAsync(UpdateItem item);

    string? FindExecutable(UpdateItem item);

    void LaunchExecutable(string path);
}

/// <summary>Row model for every update list in the app.</summary>
public partial class UpdateItemViewModel : ObservableObject
{
    private readonly IUpdateItemActions _actions;
    private PackageLinks? _links;
    private bool _linksResolved;
    private bool _executableResolved;
    private string? _executablePath;

    public UpdateItemViewModel(UpdateItem item, IUpdateItemActions actions)
    {
        Item = item;
        _actions = actions;
        IsSelected = item.CanInstallAutomatically && item.IsStable && !item.IsInstalledVersionUnknown && item.Category != UpdateCategory.Firmware;
        if (RequiresManualAction)
        {
            ResolveExecutable();
        }
    }

    public UpdateItem Item { get; }

    public string Id => Item.Id;

    public string Name => Item.Name;

    public string Source => Item.Source;

    public string? Publisher => Item.Publisher;

    public string? Description => Item.Description;

    public string VersionChange => Item.DisplayVersionChange;

    public bool HasVersionChange => !string.IsNullOrWhiteSpace(Item.DisplayVersionChange);

    public UpdateCategory Category => Item.Category;

    public bool IsApplication => Item.Category == UpdateCategory.Application;

    public string CategoryLabel => Item.Category switch
    {
        UpdateCategory.Application => "Uygulama",
        UpdateCategory.WindowsUpdate => "Windows",
        UpdateCategory.Driver => "Sürücü",
        UpdateCategory.Firmware => "BIOS / Firmware",
        _ => string.Empty,
    };

    public Wpf.Ui.Controls.SymbolRegular CategorySymbol => Item.Category switch
    {
        UpdateCategory.Application => Wpf.Ui.Controls.SymbolRegular.Apps24,
        UpdateCategory.WindowsUpdate => Wpf.Ui.Controls.SymbolRegular.ShieldCheckmark24,
        UpdateCategory.Driver => Wpf.Ui.Controls.SymbolRegular.UsbPlug24,
        UpdateCategory.Firmware => Wpf.Ui.Controls.SymbolRegular.DeveloperBoard24,
        _ => Wpf.Ui.Controls.SymbolRegular.Box24,
    };

    public bool IsInstalledVersionUnknown => Item.IsInstalledVersionUnknown;

    public bool IsStable => Item.IsStable;

    public bool IsPreRelease => !Item.IsStable;

    public bool IsFirmware => Item.Category == UpdateCategory.Firmware;

    public bool CanInstall => Item.CanInstallAutomatically;

    public bool RequiresManualAction => !Item.CanInstallAutomatically;

    public string? ManualInstructions => Item.ManualInstructions;

    public bool HasInfoUrl => !string.IsNullOrWhiteSpace(Item.InfoUrl);

    public string? InfoUrl => Item.InfoUrl;

    public string SeverityLabel => Item.Severity switch
    {
        UpdateSeverity.Critical => "Kritik",
        UpdateSeverity.Important => "Önemli",
        UpdateSeverity.Moderate => "Orta",
        UpdateSeverity.Low => "Düşük",
        _ => string.Empty,
    };

    public bool HasSeverity => Item.Severity != UpdateSeverity.Unknown;

    public bool IsCriticalOrImportant => Item.Severity is UpdateSeverity.Critical or UpdateSeverity.Important;

    public string SizeLabel => Item.SizeBytes is > 0 ? FormatBytes(Item.SizeBytes.Value) : string.Empty;

    public bool HasSize => Item.SizeBytes is > 0;

    public bool RequiresReboot => Item.RequiresReboot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsDone))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsSucceeded))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    [NotifyPropertyChangedFor(nameof(IsActionable))]
    [NotifyPropertyChangedFor(nameof(ShowInstallButton))]
    [NotifyPropertyChangedFor(nameof(ShowCheckbox))]
    [NotifyPropertyChangedFor(nameof(ShowAlternatives))]
    [NotifyPropertyChangedFor(nameof(ShowAppAlternatives))]
    [NotifyPropertyChangedFor(nameof(InstallButtonLabel))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private ItemState _state = ItemState.Pending;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private double? _progress;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAppAlternatives))]
    private bool _canLaunchApp;

    [ObservableProperty]
    private bool _isResolvingLinks;

    public bool IsBusy => State is ItemState.Installing or ItemState.Queued;

    public bool IsDone => State is ItemState.Installed or ItemState.RebootRequired or ItemState.UpToDate or ItemState.Warning;

    public bool IsWarning => State is ItemState.Warning;

    public bool IsSucceeded => IsDone && !IsWarning;

    public bool IsFailed => State is ItemState.Failed;

    public bool IsManual => State is ItemState.Manual;

    public bool IsActionable => State is ItemState.Pending or ItemState.Failed or ItemState.Cancelled or ItemState.Manual;

    /// <summary>The per-row "Güncelle" button only makes sense while the item can still be installed.</summary>
    public bool ShowInstallButton => CanInstall && IsActionable && !IsManual;

    public bool ShowCheckbox => CanInstall && !IsDone;

    /// <summary>Alternatives are offered whenever the normal path did not (or cannot) work.</summary>
    public bool ShowAlternatives => IsManual || IsFailed || IsWarning || RequiresManualAction;

    public bool ShowAppAlternatives => ShowAlternatives && IsApplication;

    public string InstallButtonLabel => State is ItemState.Failed or ItemState.Cancelled ? "Yeniden dene" : "Güncelle";

    public string StateLabel => State switch
    {
        ItemState.Pending => "Bekliyor",
        ItemState.Queued => "Sırada",
        ItemState.Installing => "Kuruluyor",
        ItemState.Installed => "Kuruldu",
        ItemState.Warning => "Tamamlandı, doğrulanamadı",
        ItemState.RebootRequired => "Yeniden başlatma gerekli",
        ItemState.UpToDate => "Zaten güncel",
        ItemState.Failed => "Başarısız",
        ItemState.Manual => "Elle güncelleme gerekli",
        ItemState.Cancelled => "İptal edildi",
        _ => string.Empty,
    };

    [RelayCommand(CanExecute = nameof(CanRunInstall))]
    private Task InstallAsync() => _actions.InstallAsync(this);

    private bool CanRunInstall() => IsActionable && CanInstall;

    [RelayCommand]
    private Task ExcludeAsync() => _actions.ExcludeAsync(this);

    [RelayCommand]
    private Task SkipVersionAsync() => _actions.SkipVersionAsync(this);

    /// <summary>Starts the installed application so its built-in updater can do the job.</summary>
    [RelayCommand]
    private void LaunchApp()
    {
        ResolveExecutable();
        if (_executablePath is null)
        {
            StatusMessage = "Uygulamanın çalıştırılabilir dosyası bulunamadı; Başlat menüsünden açın.";
            return;
        }

        try
        {
            _actions.LaunchExecutable(_executablePath);
            StatusMessage = $"Uygulama başlatıldı ({System.IO.Path.GetFileName(_executablePath)}). Kendi güncelleme seçeneğini kullanın.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Uygulama başlatılamadı: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenHomepageAsync()
    {
        var links = await ResolveLinksAsync();
        var url = links?.BestWebsite ?? Item.InfoUrl;
        if (url is null)
        {
            StatusMessage = "Resmi site adresi bulunamadı.";
            return;
        }

        OpenUrl(url);
    }

    /// <summary>Opens the official installer URL from the manifest so the user downloads the stable build from the vendor.</summary>
    [RelayCommand]
    private async Task DownloadInstallerAsync()
    {
        var links = await ResolveLinksAsync();
        if (links?.InstallerUrl is { } installer)
        {
            StatusMessage = "Resmi yükleyici tarayıcıda açıldı; indirip çalıştırın.";
            OpenUrl(installer);
            return;
        }

        var fallback = links?.BestWebsite ?? Item.InfoUrl;
        if (fallback is null)
        {
            StatusMessage = "Yükleyici adresi bulunamadı.";
            return;
        }

        StatusMessage = "Doğrudan yükleyici adresi yok; resmi site açıldı.";
        OpenUrl(fallback);
    }

    [RelayCommand]
    private void OpenInfo()
    {
        if (HasInfoUrl)
        {
            OpenUrl(Item.InfoUrl!);
        }
    }

    partial void OnStateChanged(ItemState value)
    {
        InstallCommand.NotifyCanExecuteChanged();
        if (ShowAlternatives)
        {
            ResolveExecutable();
        }
    }

    public void Apply(InstallResult result)
    {
        State = result.Outcome switch
        {
            InstallOutcome.Success => ItemState.Installed,
            InstallOutcome.SuccessWithWarning => ItemState.Warning,
            InstallOutcome.AlreadyUpToDate => ItemState.UpToDate,
            InstallOutcome.RebootRequired => ItemState.RebootRequired,
            InstallOutcome.ManualActionRequired => ItemState.Manual,
            InstallOutcome.Cancelled => ItemState.Cancelled,
            _ => ItemState.Failed,
        };
        StatusMessage = result.Message;
        Progress = null;
        if (IsDone)
        {
            IsSelected = false;
        }
    }

    private void ResolveExecutable()
    {
        if (_executableResolved || !IsApplication)
        {
            return;
        }

        _executableResolved = true;
        try
        {
            _executablePath = _actions.FindExecutable(Item);
        }
        catch (Exception)
        {
            _executablePath = null;
        }

        CanLaunchApp = _executablePath is not null;
    }

    private async Task<PackageLinks?> ResolveLinksAsync()
    {
        if (_linksResolved)
        {
            return _links;
        }

        IsResolvingLinks = true;
        try
        {
            _links = await _actions.GetLinksAsync(Item);
            _linksResolved = true;
        }
        catch (Exception)
        {
            _links = null;
        }
        finally
        {
            IsResolvingLinks = false;
        }

        return _links;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Browser launch failures are not worth surfacing.
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
