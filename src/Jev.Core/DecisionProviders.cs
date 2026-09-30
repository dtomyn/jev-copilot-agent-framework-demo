namespace Jev.Core;

/// <summary>
/// Which System-One decision service a client talks to.
///
/// All three providers speak the same <c>POST /v1/systemone</c> wire contract, so one HTTP client
/// and one set of DTOs serve all of them. Provider differences stay in
/// <see cref="SystemOneEndpoint"/> and in display-only confidence semantics; authorization policy
/// remains provider-agnostic.
/// </summary>
public enum DecisionProvider
{
    /// <summary>TypeSafe Jev, the hosted service at <c>https://api.typesafe.ai</c>.</summary>
    Jev,

    /// <summary>Laya, the open-source engine served locally by <c>laya-serve</c>.</summary>
    Laya,

    /// <summary>Mapika Decider, normally served locally by <c>decider.serve</c>.</summary>
    Decider,
}

/// <summary>
/// Everything needed to reach one System-One provider, resolved from the environment.
/// </summary>
/// <param name="Provider">The provider family, which fixes the defaults below.</param>
/// <param name="BaseAddress">Base address; the client posts to <c>v1/systemone</c> under it.</param>
/// <param name="ApiKey">
/// Bearer token, or <c>null</c> for an unauthenticated endpoint. Jev always requires one. Laya can
/// require one when configured that way; Decider's stock local server is unauthenticated.
/// </param>
/// <param name="Model">
/// Request-level model/checkpoint name, or <c>null</c> to omit the field. Laya can route requests
/// when this is omitted. Decider selects its model when the server starts via <c>DECIDER_MODEL</c>,
/// so the client intentionally omits the request-level model field for it.
/// </param>
public sealed record SystemOneEndpoint(
    DecisionProvider Provider,
    Uri BaseAddress,
    string? ApiKey,
    string? Model)
{
    public const string DefaultJevBaseAddress = "https://api.typesafe.ai/";

    // Loopback, and port 8010 rather than 8000, to match laya-local/docker-compose.yml: the
    // container listens on 8000 but publishes on 8010 because http.sys reserves 8000 on Windows.
    public const string DefaultLayaBaseAddress = "http://127.0.0.1:8010/";

    // Keep Decider on a separate loopback port so Laya and Decider can both be live in the web UI.
    public const string DefaultDeciderBaseAddress = "http://127.0.0.1:8011/";

    public const string DefaultJevModel = "jev-latest";

    public string Id => ProviderId(Provider);

    /// <summary>Human-readable provider name, used in log lines and gate reasons.</summary>
    public string DisplayName => ProviderDisplayName(Provider);

    public static string ProviderId(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => "laya",
        DecisionProvider.Decider => "decider",
        _ => "jev",
    };

    public static string ProviderDisplayName(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => "Laya",
        DecisionProvider.Decider => "Decider",
        _ => "Jev",
    };

    /// <summary>
    /// Resolves an endpoint from environment variables.
    ///
    /// <c>DECISION_PROVIDER</c> selects the provider. With nothing set, the historical behavior is
    /// preserved: a configured <c>LAYA_BASE_URL</c> wins, then <c>DECIDER_BASE_URL</c>, otherwise
    /// Jev is used. Explicit <c>--provider</c>/<c>DECISION_PROVIDER</c> is recommended when more
    /// than one local provider is configured.
    /// </summary>
    public static SystemOneEndpoint FromEnvironment(DecisionProvider? provider = null)
    {
        DecisionProvider resolved = provider ?? ResolveProviderFromEnvironment();
        return resolved switch
        {
            DecisionProvider.Laya => new SystemOneEndpoint(
                DecisionProvider.Laya,
                ReadBaseAddress("LAYA_BASE_URL", DefaultLayaBaseAddress),
                NullIfBlank(Environment.GetEnvironmentVariable("LAYA_API_KEY")),
                NullIfBlank(Environment.GetEnvironmentVariable("LAYA_MODEL"))),

            DecisionProvider.Decider => new SystemOneEndpoint(
                DecisionProvider.Decider,
                ReadBaseAddress("DECIDER_BASE_URL", DefaultDeciderBaseAddress),
                ApiKey: null,
                Model: null),

            _ => new SystemOneEndpoint(
                DecisionProvider.Jev,
                ReadBaseAddress("JEV_BASE_URL", DefaultJevBaseAddress),
                NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"))
                    ?? throw new InvalidOperationException(
                        "TYPESAFE_API_KEY is not set. Set it, or select a local provider with DECISION_PROVIDER=laya or DECISION_PROVIDER=decider."),
                NullIfBlank(Environment.GetEnvironmentVariable("JEV_MODEL")) ?? DefaultJevModel),
        };
    }

    /// <summary>
    /// The provider the environment selects, without building an endpoint. Callers that only need
    /// the family (the offline mock, tool naming) use this instead of
    /// <see cref="FromEnvironment"/>, which requires a usable Jev key.
    /// </summary>
    public static DecisionProvider ProviderFromEnvironment() => ResolveProviderFromEnvironment();

    /// <summary>
    /// Whether a live call to <paramref name="provider"/> is configured, used by the hook and UI.
    /// Jev needs a key. Local providers need their base URL exported into this process; that keeps
    /// live mode opt-in even though both have loopback defaults for direct CLI use.
    /// </summary>
    public static bool IsLiveConfigured(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => NullIfBlank(Environment.GetEnvironmentVariable("LAYA_BASE_URL")) is not null,
        DecisionProvider.Decider => NullIfBlank(Environment.GetEnvironmentVariable("DECIDER_BASE_URL")) is not null,
        _ => NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")) is not null,
    };

    /// <summary>Parses a provider name from a CLI option or environment variable.</summary>
    public static DecisionProvider Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "jev" or "typesafe" => DecisionProvider.Jev,
        "laya" => DecisionProvider.Laya,
        "decider" or "mapika" => DecisionProvider.Decider,
        _ => throw new ArgumentException(
            $"Unknown decision provider '{value}'. Use 'jev', 'laya', or 'decider'.",
            nameof(value)),
    };

    private static DecisionProvider ResolveProviderFromEnvironment()
    {
        string? explicitProvider = NullIfBlank(Environment.GetEnvironmentVariable("DECISION_PROVIDER"));
        if (explicitProvider is not null)
        {
            return Parse(explicitProvider);
        }

        // Preserve the old one-variable Laya auto-selection for existing setups.
        if (NullIfBlank(Environment.GetEnvironmentVariable("LAYA_BASE_URL")) is not null)
        {
            return DecisionProvider.Laya;
        }

        return NullIfBlank(Environment.GetEnvironmentVariable("DECIDER_BASE_URL")) is not null
            ? DecisionProvider.Decider
            : DecisionProvider.Jev;
    }

    private static Uri ReadBaseAddress(string variable, string fallback)
    {
        string raw = NullIfBlank(Environment.GetEnvironmentVariable(variable)) ?? fallback;

        // A base address without a trailing slash silently drops its last path segment when a
        // relative request URI is resolved against it, so normalize here rather than at every use.
        if (!raw.EndsWith('/'))
        {
            raw += "/";
        }

        return Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new InvalidOperationException($"{variable} is not an absolute URL: '{raw}'.");
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
