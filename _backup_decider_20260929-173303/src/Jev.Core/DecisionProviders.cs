namespace Jev.Core;

/// <summary>
/// Which System-One decision service a client talks to.
///
/// Both speak the same <c>POST /v1/systemone</c> wire contract, so one HTTP client and one set of
/// DTOs serve both. What differs is operational, and all of it lives in
/// <see cref="SystemOneEndpoint"/>: base address, whether a bearer token is required, how the
/// <c>model</c> field is interpreted, and what <c>confidence</c> means in the response.
/// </summary>
public enum DecisionProvider
{
    /// <summary>TypeSafe Jev, the hosted service at <c>https://api.typesafe.ai</c>.</summary>
    Jev,

    /// <summary>Laya, the open-source engine served locally by <c>laya-serve</c>.</summary>
    Laya,
}

/// <summary>
/// Everything needed to reach one System-One provider, resolved from the environment.
/// </summary>
/// <param name="Provider">The provider family, which fixes the defaults below.</param>
/// <param name="BaseAddress">Base address; the client posts to <c>v1/systemone</c> under it.</param>
/// <param name="ApiKey">
/// Bearer token, or <c>null</c> for an unauthenticated endpoint. Jev always requires one. Laya
/// requires one only when the server was started with <c>LAYA_API_KEY</c> set.
/// </param>
/// <param name="Model">
/// Model/checkpoint name, or <c>null</c> to omit the field entirely. Laya treats an unknown value
/// (including a Jev model id) as "let the router choose", and reports its choice in
/// <c>routing</c>; omitting the field says the same thing without the noise.
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

    public const string DefaultJevModel = "jev-latest";

    /// <summary>Human-readable provider name, used in log lines and gate reasons.</summary>
    public string DisplayName => Provider switch
    {
        DecisionProvider.Laya => "Laya",
        _ => "Jev",
    };

    /// <summary>
    /// Resolves an endpoint from environment variables.
    ///
    /// <c>DECISION_PROVIDER</c> selects the provider. With nothing set, a configured
    /// <c>LAYA_BASE_URL</c> selects Laya and otherwise Jev is used, so the original demo keeps
    /// working untouched and pointing at a local Laya container is a single variable.
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

            _ => new SystemOneEndpoint(
                DecisionProvider.Jev,
                ReadBaseAddress("JEV_BASE_URL", DefaultJevBaseAddress),
                NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"))
                    ?? throw new InvalidOperationException(
                        "TYPESAFE_API_KEY is not set. Set it, or select the open-source provider with DECISION_PROVIDER=laya."),
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
    /// Whether a live call to <paramref name="provider"/> is configured, used by the hook's
    /// <c>auto</c> mode. Jev needs a key. Laya needs an address, since an unauthenticated local
    /// server is the normal case and there is nothing else to check without a network round trip
    /// the hook has no time budget for.
    /// </summary>
    public static bool IsLiveConfigured(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => NullIfBlank(Environment.GetEnvironmentVariable("LAYA_BASE_URL")) is not null,
        _ => NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")) is not null,
    };

    /// <summary>Parses a provider name from a CLI option or environment variable.</summary>
    public static DecisionProvider Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "jev" or "typesafe" => DecisionProvider.Jev,
        "laya" => DecisionProvider.Laya,
        _ => throw new ArgumentException($"Unknown decision provider '{value}'. Use 'jev' or 'laya'.", nameof(value)),
    };

    private static DecisionProvider ResolveProviderFromEnvironment()
    {
        string? explicitProvider = NullIfBlank(Environment.GetEnvironmentVariable("DECISION_PROVIDER"));
        if (explicitProvider is not null)
        {
            return Parse(explicitProvider);
        }

        return NullIfBlank(Environment.GetEnvironmentVariable("LAYA_BASE_URL")) is not null
            ? DecisionProvider.Laya
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
