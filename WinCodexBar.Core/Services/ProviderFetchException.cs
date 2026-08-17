namespace WinCodexBar.Core.Services;

// The message stays short because it is shown on the provider card; the detail only
// ever reaches the diagnostics log.
internal sealed class ProviderFetchException : Exception
{
    public ProviderFetchException(string message, string? detail = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Detail = detail;
    }

    public string? Detail { get; }

    public static ProviderFetchException FromResponse(
        string message,
        HttpResponseMessage response,
        string? body,
        params (string Key, string? Value)[] extraFields)
    {
        return new ProviderFetchException(message, DescribeResponse(response, body, extraFields));
    }

    public static ProviderFetchException FromResponse(
        string message,
        HttpResponseMessage response,
        string? body,
        Exception innerException)
    {
        return new ProviderFetchException(message, DescribeResponse(response, body), innerException);
    }

    private static string? DescribeResponse(
        HttpResponseMessage response,
        string? body,
        params (string Key, string? Value)[] extraFields)
    {
        var fields = new List<(string Key, string? Value)>
        {
            ("url", response.RequestMessage?.RequestUri?.ToString()),
            ("status", $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim()),
            ("body", DiagnosticsDetail.Body(body))
        };
        fields.AddRange(extraFields);

        return DiagnosticsDetail.Compose(fields.ToArray());
    }
}
