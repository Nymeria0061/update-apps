using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using UpdateHub.Core.Abstractions;
using UpdateHub.Core.Models;
using Wpf.Ui.Abstractions.Controls;

namespace UpdateHub.App.ViewModels;

public sealed class DeviceRowViewModel
{
    public DeviceRowViewModel(DeviceInfo device)
    {
        Device = device;
    }

    public DeviceInfo Device { get; }

    public string Name => Device.Name;

    public string DeviceClass => Device.DeviceClass;

    public string Manufacturer => string.IsNullOrEmpty(Device.DriverProvider) ? Device.Manufacturer : Device.DriverProvider;

    public string DriverVersion => string.IsNullOrEmpty(Device.DriverVersion) ? "—" : Device.DriverVersion;

    public string DriverDate => Device.DriverDate?.ToString("dd.MM.yyyy") ?? string.Empty;

    public bool HasProblem => Device.Status == DeviceStatus.Problem;

    public bool IsDisabled => Device.Status == DeviceStatus.Disabled;

    public bool IsUnsigned => !Device.IsSigned && !string.IsNullOrEmpty(Device.DriverVersion);

    public string StatusLabel => Device.Status switch
    {
        DeviceStatus.Ok => "Çalışıyor",
        DeviceStatus.Problem => $"Sorun (kod {Device.ProblemCode})",
        DeviceStatus.Disabled => "Devre dışı",
        _ => string.Empty,
    };

    public string SignerLabel => Device.IsSigned ? (string.IsNullOrEmpty(Device.Signer) ? "İmzalı" : Device.Signer) : "İmzasız";
}

/// <summary>Inventory of installed drivers (read-only); updates for them come from the Windows Update page.</summary>
public sealed partial class DevicesViewModel : ObservableObject, INavigationAware
{
    private readonly IDeviceInventory _inventory;
    private readonly ILogger<DevicesViewModel> _logger;

    public DevicesViewModel(IDeviceInventory inventory, ILogger<DevicesViewModel> logger)
    {
        _inventory = inventory;
        _logger = logger;
        View = new ListCollectionView(Devices) { Filter = Filter };
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceRowViewModel.DeviceClass)));
    }

    public ObservableCollection<DeviceRowViewModel> Devices { get; } = new();

    public ICollectionView View { get; }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _problemsOnly;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _problemCount;

    [ObservableProperty]
    private int _unsignedCount;

    partial void OnSearchTextChanged(string value) => View.Refresh();

    partial void OnProblemsOnlyChanged(bool value) => View.Refresh();

    private bool Filter(object obj)
    {
        if (obj is not DeviceRowViewModel row)
        {
            return false;
        }

        if (ProblemsOnly && !row.HasProblem && !row.IsUnsigned)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(SearchText)
               || row.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || row.DeviceClass.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
               || row.Manufacturer.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);
    }

    public async Task OnNavigatedToAsync()
    {
        if (Devices.Count == 0)
        {
            await RefreshAsync();
        }
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            var devices = await _inventory.GetDevicesAsync(CancellationToken.None);
            Devices.Clear();
            foreach (var d in devices)
            {
                Devices.Add(new DeviceRowViewModel(d));
            }

            TotalCount = Devices.Count;
            ProblemCount = Devices.Count(d => d.HasProblem);
            UnsignedCount = Devices.Count(d => d.IsUnsigned);
            View.Refresh();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Device inventory failed");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenDeviceManager()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }
}
