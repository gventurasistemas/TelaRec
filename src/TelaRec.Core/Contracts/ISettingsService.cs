using TelaRec.Core.Configuration;

namespace TelaRec.Core.Contracts;

public interface ISettingsService
{
    Task<TelaRecSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TelaRecSettings settings, CancellationToken cancellationToken = default);
}
