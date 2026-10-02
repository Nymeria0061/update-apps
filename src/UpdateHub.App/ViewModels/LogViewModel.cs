using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UpdateHub.App.Services;
using UpdateHub.Core.Services;

namespace UpdateHub.App.ViewModels;

public sealed partial class LogViewModel : ObservableObject
{
    public LogViewModel(LogStore store)
    {
        Store = store;
    }

    public LogStore Store { get; }

    public string LogFile => Store.FilePath ?? "(dosya günlüğü kapalı)";

    [RelayCommand]
    private void Clear() => Store.Clear();

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        var text = string.Join(Environment.NewLine, Store.Entries.Select(e => $"{e.Time} [{e.LevelLabel}] {e.ShortCategory}: {e.Message}"));
        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch (Exception)
        {
        }
    }
}
