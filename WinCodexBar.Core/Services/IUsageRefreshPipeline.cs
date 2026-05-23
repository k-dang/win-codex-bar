using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public interface IUsageRefreshPipeline
{
    Task<UsageSummary> RefreshAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task<UsageSummary> RefreshProviderAsync(
        AppSettings settings,
        UsageSummary currentSummary,
        ProviderKind provider,
        CancellationToken cancellationToken = default);
}
