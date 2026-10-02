using System.Runtime.Versioning;
using UpdateHub.Core.Abstractions;

namespace UpdateHub.Windows.Services;

public interface IRebootService
{
    /// <summary>Schedules a restart in <paramref name="delay"/>; the user can cancel with "shutdown /a".</summary>
    Task RestartAsync(TimeSpan delay, CancellationToken cancellationToken);

    Task CancelRestartAsync(CancellationToken cancellationToken);
}

[SupportedOSPlatform("windows")]
public sealed class RebootService : IRebootService
{
    private readonly IProcessRunner _runner;

    public RebootService(IProcessRunner runner)
    {
        _runner = runner;
    }

    public Task RestartAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        _runner.RunAsync(new ProcessRequest(ShutdownExe(),
            ["/r", "/t", ((int)delay.TotalSeconds).ToString(), "/c", "UpdateHub: güncellemeleri tamamlamak için yeniden başlatılıyor."]), cancellationToken);

    public Task CancelRestartAsync(CancellationToken cancellationToken) =>
        _runner.RunAsync(new ProcessRequest(ShutdownExe(), ["/a"]), cancellationToken);

    private static string ShutdownExe() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
}
