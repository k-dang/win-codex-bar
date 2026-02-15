using System.Collections.Generic;

namespace WinCodexBar.Core.Models;

public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderKind> SupportedProviders = new[]
    {
        ProviderKind.Codex,
        ProviderKind.Claude
    };
}

