using System.Threading;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public interface IProviderUsageFetcher
{
    ProviderKind Kind { get; }

    Task<ProviderUsageSnapshot?> FetchAsync(
        AppSettings appSettings,
        ProviderSettings providerSettings,
        CancellationToken cancellationToken);
}
