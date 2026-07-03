using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace WinCodexBar.Core.Services;

internal interface ICursorLocalTokenReader
{
    string? ReadAccessToken();
}

internal sealed class CursorStateDbTokenReader : ICursorLocalTokenReader
{
    public string? ReadAccessToken()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cursor", "User", "globalStorage", "state.vscdb");

        if (!File.Exists(dbPath))
        {
            return null;
        }

        try
        {
            // The DB is large and held open by a running Cursor, so open read-only
            // with a short busy timeout and treat any failure as "no local token".
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 1
            }.ToString();

            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1";
            command.Parameters.AddWithValue("$key", "cursorAuth/accessToken");

            return command.ExecuteScalar() switch
            {
                string text => text,
                byte[] blob => Encoding.UTF8.GetString(blob),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record CursorAppSession(string UserId, string AccessToken)
{
    // Cursor's API is cookie-only: even the local app JWT is sent as the value half of
    // the WorkosCursorSessionToken cookie, with the "::" separator URL-encoded.
    public string CookieHeader => $"WorkosCursorSessionToken={UserId}%3A%3A{AccessToken}";

    public static CursorAppSession? TryCreate(string? accessToken, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var payload = TryDecodeJwtPayload(accessToken);
        if (payload == null || payload.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // There is no refresh flow for the app token, so a token at/near expiry is unusable.
        if (!payload.RootElement.TryGetProperty("exp", out var exp) ||
            exp.ValueKind != JsonValueKind.Number ||
            !exp.TryGetInt64(out var expUnixSeconds) ||
            DateTimeOffset.FromUnixTimeSeconds(expUnixSeconds) <= now.AddSeconds(60))
        {
            return null;
        }

        var userId = payload.RootElement.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.String
            ? TryExtractUserId(sub.GetString())
            : null;

        return userId == null ? null : new CursorAppSession(userId, accessToken);
    }

    internal static string? TryExtractUserId(string? subject)
    {
        if (string.IsNullOrEmpty(subject))
        {
            return null;
        }

        // e.g. "google-oauth2|user_01ABC" -> "user_01ABC"
        var segments = subject.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        var userId = segments[^1];
        return userId.All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? userId
            : null;
    }

    private static JsonDocument? TryDecodeJwtPayload(string token)
    {
        var parts = token.Split('.');
        if (parts.Length < 2 || parts[1].Length == 0)
        {
            return null;
        }

        try
        {
            return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
