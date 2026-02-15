using System.Collections.Generic;
using System.Net.Http;

namespace WinCodexBar.Core.Services;

internal static class ProviderFetcherFactory
{
    public static IReadOnlyList<IProviderUsageFetcher> CreateDefault(HttpClient httpClient, IDiagnosticsLogger? logger = null)
    {
        return new List<IProviderUsageFetcher>
        {
            new CodexProviderUsageFetcher(httpClient, logger),
            new ClaudeProviderUsageFetcher(httpClient, logger)
        };
    }
}

