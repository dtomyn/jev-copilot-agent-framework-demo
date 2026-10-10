using Jev.Core;

namespace Jev.WebUi;

/// <summary>
/// Turns the browser's two dropdowns, provider and mode, into an <see cref="IJevClient"/>.
///
/// The UI picks per request rather than per process on purpose: a presenter can switch among Laya,
/// Decider, Clef, and Jev, or between mock and live, mid-demo. The same policy code can therefore
/// be exercised against all four engines back to back without a restart.
/// </summary>
public static class DecisionSession
{
    public const string DefaultProvider = "laya";
    public const string DefaultMode = "mock";

    /// <summary>
    /// Thrown for a selection the environment cannot satisfy, such as live Jev with no API key.
    /// The caller turns it into a 400 with the message shown in the UI: misconfiguration is a
    /// normal state during a demo, not a server fault.
    /// </summary>
    public sealed class UnavailableException(string message) : Exception(message);

    public static DecisionProvider ParseProvider(string? value)
    {
        try
        {
            return SystemOneEndpoint.Parse(string.IsNullOrWhiteSpace(value) ? DefaultProvider : value);
        }
        catch (ArgumentException ex)
        {
            throw new UnavailableException(ex.Message);
        }
    }

    public static string NormalizeMode(string? value)
    {
        string mode = (string.IsNullOrWhiteSpace(value) ? DefaultMode : value).Trim().ToLowerInvariant();
        return mode is "mock" or "live"
            ? mode
            : throw new UnavailableException($"Mode must be 'mock' or 'live', not '{value}'.");
    }

    /// <summary>
    /// Creates a client for one request. The caller disposes it: the live client owns an
    /// <see cref="HttpClient"/>, the mock owns nothing.
    /// </summary>
    public static IJevClient Create(string? provider, string? mode, out string providerId, out string normalizedMode, WireLog? wire = null)
    {
        DecisionProvider resolved = ParseProvider(provider);
        providerId = SystemOneEndpoint.ProviderId(resolved);
        normalizedMode = NormalizeMode(mode);

        if (normalizedMode == "mock")
        {
            return new MockJevClient(resolved);
        }

        try
        {
            // Short, because a browser is waiting. A cold local model container can take much
            // longer to load than this; its startup scripts wait for /health before enabling live mode.
            SystemOneHttpClient live = SystemOneHttpClient.FromEnvironment(resolved, TimeSpan.FromSeconds(20));
            if (wire is not null)
            {
                live.OnExchange = wire.Add;
            }

            return live;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            throw new UnavailableException(ex.Message);
        }
    }

    /// <summary>
    /// Collects the exchanges one request performs, for the diagnostics area. The gate can make
    /// several provider calls per request, so this is a list rather than a single slot.
    /// </summary>
    public sealed class WireLog
    {
        private readonly List<WireExchange> _exchanges = [];

        public IReadOnlyList<WireExchange> Exchanges => _exchanges;

        public void Add(WireExchange exchange) => _exchanges.Add(exchange);
    }

    /// <summary>A failed live call, carrying what went over the wire so the UI can still show it.</summary>
    public sealed class WireFailureException(Exception inner, IReadOnlyList<WireExchange> wire)
        : Exception(inner.Message, inner)
    {
        public IReadOnlyList<WireExchange> Wire { get; } = wire;
    }

    public static string DisplayName(DecisionProvider provider) =>
        SystemOneEndpoint.ProviderDisplayName(provider);

    /// <summary>
    /// Whether live mode is configured for each provider, and what the presenter has to do about
    /// it if not. Mock mode always works, which is why it is the default.
    /// </summary>
    public static IReadOnlyList<ProviderStatus> Statuses()
    {
        bool layaLive = SystemOneEndpoint.IsLiveConfigured(DecisionProvider.Laya);
        bool deciderLive = SystemOneEndpoint.IsLiveConfigured(DecisionProvider.Decider);
        bool clefLive = SystemOneEndpoint.IsLiveConfigured(DecisionProvider.Clef);
        bool microsoftLive = SystemOneEndpoint.IsLiveConfigured(DecisionProvider.Microsoft);
        bool jevLive = SystemOneEndpoint.IsLiveConfigured(DecisionProvider.Jev);

        return
        [
            new ProviderStatus(
                "laya",
                "Laya",
                layaLive,
                layaLive
                    ? $"Live requests go to {Environment.GetEnvironmentVariable("LAYA_BASE_URL")}."
                    : "Set LAYA_BASE_URL and start laya-serve (scripts/laya-up.ps1) to enable live mode."),
            new ProviderStatus(
                "decider",
                "Decider",
                deciderLive,
                deciderLive
                    ? $"Live requests go to {Environment.GetEnvironmentVariable("DECIDER_BASE_URL")}."
                    : "Set DECIDER_BASE_URL and start decider.serve (scripts/decider-up.ps1) to enable live mode."),
            new ProviderStatus(
                "clef",
                "Clef",
                clefLive,
                ClefStatusMessage(clefLive)),
            new ProviderStatus(
                "microsoft",
                "Microsoft Decision",
                microsoftLive,
                microsoftLive
                    ? $"Live requests go to {Environment.GetEnvironmentVariable("MICROSOFT_DECISION_ENDPOINT")} (Microsoft Foundry)."
                    : "Set MICROSOFT_DECISION_ENDPOINT, MICROSOFT_DECISION_DEPLOYMENT and MICROSOFT_DECISION_API_KEY (or MICROSOFT_DECISION_ACCESS_TOKEN) to enable live mode. See docs/MICROSOFT.md."),
            new ProviderStatus(
                "jev",
                "Jev",
                jevLive,
                jevLive
                    ? "Live requests go to the hosted TypeSafe endpoint."
                    : "Enter a TypeSafe API key under Config, or set TYPESAFE_API_KEY, to enable live mode. Mock mode needs no key."),
        ];
    }

    // Clef can be live two ways, and the presenter should be able to tell which one before
    // pressing Run: one of them sends the state to Cloudflare.
    private static string ClefStatusMessage(bool live)
    {
        if (!live)
        {
            return "Set CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN for hosted Workers AI, or CLEF_BASE_URL for a local container (scripts/clef-up.ps1), to enable live mode.";
        }

        string? local = Environment.GetEnvironmentVariable("CLEF_BASE_URL");
        return string.IsNullOrWhiteSpace(local)
            ? "Live requests go to Cloudflare Workers AI (hosted)."
            : $"Live requests go to {local}.";
    }
}
