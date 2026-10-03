namespace Jev.Core;

/// <summary>
/// Which System-One decision service a client talks to.
///
/// All four providers speak the same System-One wire contract, so one HTTP client and one set of
/// DTOs serve all of them. Provider differences, including the route and response envelope of
/// hosted Clef on Cloudflare Workers AI, stay in <see cref="SystemOneEndpoint"/> and in
/// display-only confidence semantics; authorization policy remains provider-agnostic.
/// </summary>
public enum DecisionProvider
{
    /// <summary>TypeSafe Jev, the hosted service at <c>https://api.typesafe.ai</c>.</summary>
    Jev,

    /// <summary>Laya, the open-source engine served locally by <c>laya-serve</c>.</summary>
    Laya,

    /// <summary>Mapika Decider, normally served locally by <c>decider.serve</c>.</summary>
    Decider,

    /// <summary>
    /// Cloudflare Clef (<c>clef-flash</c> by default), hosted on Workers AI or served locally by
    /// the container in <c>docker/clef</c>.
    /// </summary>
    Clef,
}

/// <summary>
/// Everything needed to reach one System-One provider, resolved from the environment.
/// </summary>
/// <param name="Provider">The provider family, which fixes the defaults below.</param>
/// <param name="BaseAddress">Base address; the client posts to <paramref name="RequestPath"/> under it.</param>
/// <param name="ApiKey">
/// Bearer token, or <c>null</c> for an unauthenticated endpoint. Jev always requires one. Laya can
/// require one when configured that way; Decider's stock local server is unauthenticated. Hosted
/// Clef always requires a Cloudflare API token; the local Clef container can opt into one.
/// </param>
/// <param name="Model">
/// Request-level model/checkpoint name, or <c>null</c> to omit the field. Laya can route requests
/// when this is omitted. Decider selects its model when the server starts via <c>DECIDER_MODEL</c>,
/// so the client intentionally omits the request-level model field for it. Clef requires it.
/// </param>
/// <param name="RequestPath">
/// Path of the decision route, relative to <paramref name="BaseAddress"/>. Every self-hosted server
/// uses <c>v1/systemone</c>; Cloudflare Workers AI routes by model name instead.
/// </param>
/// <param name="ResultEnvelope">
/// Whether the response body is wrapped in Cloudflare's <c>{ "success", "errors", "result" }</c>
/// API envelope, with the System-One response under <c>result</c>.
/// </param>
public sealed record SystemOneEndpoint(
    DecisionProvider Provider,
    Uri BaseAddress,
    string? ApiKey,
    string? Model,
    string RequestPath = SystemOneEndpoint.DefaultRequestPath,
    bool ResultEnvelope = false)
{
    public const string DefaultRequestPath = "v1/systemone";

    public const string DefaultJevBaseAddress = "https://api.typesafe.ai/";

    // Loopback, and port 8010 rather than 8000, to match laya-local/docker-compose.yml: the
    // container listens on 8000 but publishes on 8010 because http.sys reserves 8000 on Windows.
    public const string DefaultLayaBaseAddress = "http://127.0.0.1:8010/";

    // Keep Decider on a separate loopback port so Laya and Decider can both be live in the web UI.
    public const string DefaultDeciderBaseAddress = "http://127.0.0.1:8011/";

    // The next loopback port, so the local Clef container can be live alongside the other two.
    public const string DefaultClefBaseAddress = "http://127.0.0.1:8012/";

    public const string CloudflareApiBaseAddress = "https://api.cloudflare.com/client/v4/";

    public const string DefaultJevModel = "jev-latest";

    // Clef rejects a request without `model`, and the hosted Workers AI route is named after it.
    public const string DefaultClefModel = "clef-flash";

    public string Id => ProviderId(Provider);

    /// <summary>Human-readable provider name, used in log lines and gate reasons.</summary>
    public string DisplayName => ProviderDisplayName(Provider);

    public static string ProviderId(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => "laya",
        DecisionProvider.Decider => "decider",
        DecisionProvider.Clef => "clef",
        _ => "jev",
    };

    public static string ProviderDisplayName(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => "Laya",
        DecisionProvider.Decider => "Decider",
        DecisionProvider.Clef => "Clef",
        _ => "Jev",
    };

    /// <summary>
    /// Resolves an endpoint from environment variables.
    ///
    /// <c>DECISION_PROVIDER</c> selects the provider. With nothing set, the historical behavior is
    /// preserved: a configured <c>LAYA_BASE_URL</c> wins, then <c>DECIDER_BASE_URL</c>, then
    /// <c>CLEF_BASE_URL</c>, otherwise Jev is used. Explicit <c>--provider</c>/<c>DECISION_PROVIDER</c>
    /// is recommended when more than one local provider is configured.
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

            DecisionProvider.Clef => ClefFromEnvironment(),

            _ => new SystemOneEndpoint(
                DecisionProvider.Jev,
                ReadBaseAddress("JEV_BASE_URL", DefaultJevBaseAddress),
                NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY"))
                    ?? throw new InvalidOperationException(
                        "TYPESAFE_API_KEY is not set. Set it, or select another provider with DECISION_PROVIDER=laya, decider, or clef."),
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
    /// live mode opt-in even though they have loopback defaults for direct CLI use. Clef is live
    /// with either a local <c>CLEF_BASE_URL</c> or both Cloudflare variables.
    /// </summary>
    public static bool IsLiveConfigured(DecisionProvider provider) => provider switch
    {
        DecisionProvider.Laya => NullIfBlank(Environment.GetEnvironmentVariable("LAYA_BASE_URL")) is not null,
        DecisionProvider.Decider => NullIfBlank(Environment.GetEnvironmentVariable("DECIDER_BASE_URL")) is not null,
        DecisionProvider.Clef => NullIfBlank(Environment.GetEnvironmentVariable("CLEF_BASE_URL")) is not null ||
            (NullIfBlank(Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID")) is not null &&
             NullIfBlank(Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN")) is not null),
        _ => NullIfBlank(Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")) is not null,
    };

    /// <summary>Parses a provider name from a CLI option or environment variable.</summary>
    public static DecisionProvider Parse(string value) => value.Trim().ToLowerInvariant() switch
    {
        "jev" or "typesafe" => DecisionProvider.Jev,
        "laya" => DecisionProvider.Laya,
        "decider" or "mapika" => DecisionProvider.Decider,
        "clef" or "clef-flash" => DecisionProvider.Clef,
        _ => throw new ArgumentException(
            $"Unknown decision provider '{value}'. Use 'jev', 'laya', 'decider', or 'clef'.",
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

        if (NullIfBlank(Environment.GetEnvironmentVariable("DECIDER_BASE_URL")) is not null)
        {
            return DecisionProvider.Decider;
        }

        // Only a local Clef server auto-selects. Cloudflare credentials alone never do: they are
        // commonly present for other reasons, and picking them up would start sending every tool
        // call to a hosted service that nobody explicitly chose.
        return NullIfBlank(Environment.GetEnvironmentVariable("CLEF_BASE_URL")) is not null
            ? DecisionProvider.Clef
            : DecisionProvider.Jev;
    }

    /// <summary>
    /// Clef has two deployments of one model. <c>CLEF_BASE_URL</c> means a self-hosted server
    /// speaking plain <c>POST /v1/systemone</c> (the container in <c>docker/clef</c>), and it takes
    /// precedence, so setting it can never leak a call to Cloudflare. Otherwise the hosted Workers
    /// AI route is used: it needs an account ID and an API token, is named after the model, and
    /// wraps the System-One response in Cloudflare's API envelope.
    /// </summary>
    private static SystemOneEndpoint ClefFromEnvironment()
    {
        string model = NullIfBlank(Environment.GetEnvironmentVariable("CLEF_MODEL"))?.Trim() ?? DefaultClefModel;
        if (model is not ("clef" or "clef-flash"))
        {
            throw new InvalidOperationException($"CLEF_MODEL must be 'clef' or 'clef-flash', not '{model}'.");
        }

        if (NullIfBlank(Environment.GetEnvironmentVariable("CLEF_BASE_URL")) is not null)
        {
            return new SystemOneEndpoint(
                DecisionProvider.Clef,
                ReadBaseAddress("CLEF_BASE_URL", DefaultClefBaseAddress),
                NullIfBlank(Environment.GetEnvironmentVariable("CLEF_API_KEY")),
                model);
        }

        string accountId = NullIfBlank(Environment.GetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID"))?.Trim()
            ?? throw new InvalidOperationException(
                "Clef needs CLEF_BASE_URL for a local server, or CLOUDFLARE_ACCOUNT_ID and CLOUDFLARE_API_TOKEN for Workers AI.");

        // The account ID becomes a path segment; refuse anything that could change the route.
        if (!accountId.All(char.IsAsciiLetterOrDigit))
        {
            throw new InvalidOperationException("CLOUDFLARE_ACCOUNT_ID must contain only letters and digits.");
        }

        string token = NullIfBlank(Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN"))
            ?? throw new InvalidOperationException(
                "CLOUDFLARE_API_TOKEN is not set. Create a Workers AI token, or set CLEF_BASE_URL for a local server.");

        return new SystemOneEndpoint(
            DecisionProvider.Clef,
            new Uri($"{CloudflareApiBaseAddress}accounts/{accountId}/"),
            token,
            model,
            RequestPath: $"ai/run/@cf/cloudflare/{model}",
            ResultEnvelope: true);
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
