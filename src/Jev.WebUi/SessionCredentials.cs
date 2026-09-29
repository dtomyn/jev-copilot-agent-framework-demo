namespace Jev.WebUi;

/// <summary>
/// Lets the presenter supply the TypeSafe API key from the page instead of from the shell that
/// started the UI.
///
/// The key is applied exactly the way the shell applies it, as the <c>TYPESAFE_API_KEY</c>
/// variable of this process, so <see cref="Jev.Core.SystemOneEndpoint"/>, the live-mode check, and
/// every hook and level process the UI starts (which inherit this environment) see it with no
/// second code path. It lives in this process's memory only: nothing writes it to disk, and no
/// endpoint ever returns it, not even partially, because the page is usually on a projector.
/// </summary>
public static class SessionCredentials
{
    public const string JevApiKeyVariable = "TYPESAFE_API_KEY";

    // Far longer than any real key; a limit only so a pasted document is refused rather than
    // copied into the environment block of every child process.
    private const int MaxKeyLength = 1024;

    private static readonly Lock s_gate = new();
    private static string? s_source;

    // A static constructor rather than a field initializer, so the startup value is captured
    // before any member runs, not lazily after the page has already changed it.
    static SessionCredentials() =>
        s_source = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JevApiKeyVariable))
            ? null
            : "environment";

    public static ApiKeyStatus JevApiKey()
    {
        lock (s_gate)
        {
            return new ApiKeyStatus(s_source is not null, s_source);
        }
    }

    public static void SetJevApiKey(string? value)
    {
        string key = (value ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            throw new DecisionSession.UnavailableException("Enter a key, or use Clear key to remove the current one.");
        }

        if (key.Length > MaxKeyLength)
        {
            throw new DecisionSession.UnavailableException($"That is longer than {MaxKeyLength} characters, which is not an API key.");
        }

        if (key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new DecisionSession.UnavailableException("An API key cannot contain spaces or line breaks. Paste the key on its own.");
        }

        lock (s_gate)
        {
            Environment.SetEnvironmentVariable(JevApiKeyVariable, key);
            s_source = "page";
        }
    }

    public static void ClearJevApiKey()
    {
        lock (s_gate)
        {
            Environment.SetEnvironmentVariable(JevApiKeyVariable, null);
            s_source = null;
        }
    }
}
