using UpdateHub.Core.Models;

namespace UpdateHub.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler? Changed;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);
}
