using System.Collections.Generic;

namespace tray_ui.Models;

public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderKind> SupportedProviders = new[]
    {
        ProviderKind.Codex,
        ProviderKind.Claude
    };
}
