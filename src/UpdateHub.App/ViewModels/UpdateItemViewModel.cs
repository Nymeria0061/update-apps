using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.Core.Models;

namespace UpdateHub.App.ViewModels;

public enum ItemState
{
    Pending,
    Queued,
    Installing,
    Installed,
    RebootRequired,
    UpToDate,
    Failed,
    Manual,
    Cancelled,
}

/// <summary>Row model for every update list in the app.</summary>
public partial class UpdateItemViewModel : ObservableObject
{
    private readonly Func<UpdateItemViewModel, Task> _install;
    private readonly Func<UpdateItemViewModel, Task> _exclude;

    public UpdateItemViewModel(UpdateItem item, Func<UpdateItemViewModel, Task> install, Func<UpdateItemViewModel, Task> exclude)
    {
        Item = item;
        _install = install;
        _exclude = exclude;
        IsSelected = item.CanInstallAutomatically && item.IsStable && item.Category != UpdateCategory.Firmware;
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
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsActionable))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    private ItemState _state = ItemState.Pending;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private double? _progress;

    [ObservableProperty]
    private bool _isSelected;

    public bool IsBusy => State is ItemState.Installing or ItemState.Queued;

    public bool IsDone => State is ItemState.Installed or ItemState.RebootRequired or ItemState.UpToDate;

    public bool IsFailed => State is ItemState.Failed;

    public bool IsActionable => State is ItemState.Pending or ItemState.Failed or ItemState.Cancelled or ItemState.Manual;

    public string StateLabel => State switch
    {
        ItemState.Pending => "Bekliyor",
        ItemState.Queued => "Sırada",
        ItemState.Installing => "Kuruluyor",
        ItemState.Installed => "Kuruldu",
        ItemState.RebootRequired => "Yeniden başlatma gerekli",
        ItemState.UpToDate => "Zaten güncel",
        ItemState.Failed => "Başarısız",
        ItemState.Manual => "Elle kurulum gerekli",
        ItemState.Cancelled => "İptal edildi",
        _ => string.Empty,
    };

    [RelayCommand(CanExecute = nameof(CanRunInstall))]
    private Task InstallAsync() => _install(this);

    private bool CanRunInstall() => IsActionable && CanInstall;

    [RelayCommand]
    private Task ExcludeAsync() => _exclude(this);

    [RelayCommand]
    private void OpenInfo()
    {
        if (!HasInfoUrl)
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Item.InfoUrl!) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Browser launch failures are not worth surfacing.
        }
    }

    partial void OnStateChanged(ItemState value) => InstallCommand.NotifyCanExecuteChanged();

    public void Apply(InstallResult result)
    {
        State = result.Outcome switch
        {
            InstallOutcome.Success => ItemState.Installed,
            InstallOutcome.AlreadyUpToDate => ItemState.UpToDate,
            InstallOutcome.RebootRequired => ItemState.RebootRequired,
            InstallOutcome.ManualActionRequired => ItemState.Manual,
            InstallOutcome.Cancelled => ItemState.Cancelled,
            _ => ItemState.Failed,
        };
        StatusMessage = result.Message;
        Progress = null;
        if (State is ItemState.Installed or ItemState.RebootRequired or ItemState.UpToDate)
        {
            IsSelected = false;
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
