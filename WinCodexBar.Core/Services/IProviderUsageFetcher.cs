using System.Threading;
using System.Threading.Tasks;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public interface IProviderUsageFetcher
{
    ProviderKind Kind { get; }

    Task<ProviderUsageSnapshot?> FetchAsync(
        AppSettings appSettings,
        ProviderSettings providerSettings,
        CancellationToken cancellationToken);
}


