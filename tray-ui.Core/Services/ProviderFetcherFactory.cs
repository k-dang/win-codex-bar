using System.Collections.Generic;
using System.Net.Http;

namespace tray_ui.Services;

internal static class ProviderFetcherFactory
{
    public static IReadOnlyList<IProviderUsageFetcher> CreateDefault(HttpClient httpClient)
    {
        return new List<IProviderUsageFetcher>
        {
            new CodexProviderUsageFetcher(httpClient),
            new ClaudeProviderUsageFetcher(httpClient)
        };
    }
}
