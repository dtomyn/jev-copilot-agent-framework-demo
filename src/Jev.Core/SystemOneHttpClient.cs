using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Jev.Core;

/// <summary>
/// One client for the <c>POST /v1/systemone</c> System-One contract shared by TypeSafe Jev,
/// Laya, Mapika Decider, Cloudflare Clef, and Microsoft-Decision-1. Everything provider-specific is in the
/// <see cref="SystemOneEndpoint"/> it is constructed with.
/// </summary>
public sealed class SystemOneHttpClient : IJevClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly SystemOneEndpoint _endpoint;

    public SystemOneHttpClient(HttpClient http, SystemOneEndpoint endpoint)
        : this(http, endpoint, ownsHttp: false)
    {
    }

    private SystemOneHttpClient(HttpClient http, SystemOneEndpoint endpoint, bool ownsHttp)
    {
        _http = http;
        _endpoint = endpoint;
        _ownsHttp = ownsHttp;

        if (endpoint.Provider == DecisionProvider.Jev && string.IsNullOrWhiteSpace(endpoint.ApiKey))
        {
            throw new ArgumentException("A TypeSafe API key is required for the Jev provider.", nameof(endpoint));
        }
    }

    public DecisionProvider Provider => _endpoint.Provider;

    /// <summary>
    /// Receives every exchange this client performs, successful or not, with credentials masked.
    /// Off by default; the web UI sets it to feed its diagnostics area.
    /// </summary>
    public Action<WireExchange>? OnExchange { get; set; }

    public string Description => $"{_endpoint.DisplayName} live ({_endpoint.BaseAddress}" +
        $"{(_endpoint.Model is null ? ", provider-selected model" : $", model={_endpoint.Model}")}" +
        $"{(_endpoint.ApiKey is null ? ", no bearer token" : string.Empty)})";

    public static SystemOneHttpClient FromEnvironment(
        DecisionProvider? provider = null,
        TimeSpan? timeout = null)
    {
        SystemOneEndpoint endpoint = SystemOneEndpoint.FromEnvironment(provider);
        var http = new HttpClient
        {
            BaseAddress = endpoint.BaseAddress,
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

        // This overload created the HttpClient, so the returned instance owns and disposes it.
        return new SystemOneHttpClient(http, endpoint, ownsHttp: true);
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    public async Task<SystemOneResponse> DecideAsync(
        object state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one decision question is required.", nameof(questions));
        }

        var payload = new SystemOneRequest(state, _endpoint.Model, questions);
        // Serialize up front so the request carries a Content-Length. JsonContent streams the body
        // with chunked transfer encoding, which gateways in front of a provider (Azure API
        // Management, for one) reject with a 400 or by resetting the connection.
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, JevJson.Options);
        var content = new ByteArrayContent(json);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.RequestPath)
        {
            Content = content,
        };

        // Local providers normally use no bearer token: Laya and local Clef can opt into one, and
        // Decider's stock server is unauthenticated. Hosted Clef always sends a Cloudflare API
        // token, and Microsoft Decision sends a Foundry key or Entra token. Never send an empty
        // Authorization header.
        if (_endpoint.ApiKey is { Length: > 0 } apiKey)
        {
            if (_endpoint.ApiKeyHeader is { Length: > 0 } header)
            {
                // Foundry's key auth: the raw key in `api-key`, not a bearer token.
                request.Headers.Add(header, apiKey);
            }
            else
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        string url = new Uri(_endpoint.BaseAddress, _endpoint.RequestPath).AbsoluteUri;
        List<WireHeader> requestHeaders = [.. DescribeHeaders(request.Headers), .. DescribeHeaders(content.Headers)];
        long started = Stopwatch.GetTimestamp();

        HttpResponseMessage response;
        string body;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Publish(new WireExchange(
                "POST", url, requestHeaders, Encoding.UTF8.GetString(json), null, null, [], null,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                ex.InnerException is null ? ex.Message : $"{ex.Message} {ex.InnerException.Message}"));
            throw;
        }

        using (response)
        {
            Publish(new WireExchange(
                "POST", url, requestHeaders, Encoding.UTF8.GetString(json),
                (int)response.StatusCode, response.ReasonPhrase,
                [.. DescribeHeaders(response.Headers), .. DescribeHeaders(response.Content.Headers)],
                body, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, null));

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"{_endpoint.DisplayName} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }
        }

        return ParseResponse(body, _endpoint.ResultEnvelope);
    }

    private void Publish(WireExchange exchange) => OnExchange?.Invoke(exchange);

    private static IEnumerable<WireHeader> DescribeHeaders(HttpHeaders headers)
    {
        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {
            string value = string.Join(", ", header.Value);
            yield return new WireHeader(header.Key, IsCredential(header.Key) ? Mask(value) : value);
        }
    }

    private static bool IsCredential(string name) =>
        name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("api-key", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase);

    // Keeps the scheme so "Bearer" versus a raw key is still visible, and the length so a truncated
    // paste is noticeable. Never any part of the secret.
    private static string Mask(string value)
    {
        int space = value.IndexOf(' ');
        string scheme = space > 0 ? value[..(space + 1)] : string.Empty;
        return $"{scheme}<redacted, {value.Length - scheme.Length} chars>";
    }

    /// <summary>
    /// Parses a System-One response body. With <paramref name="resultEnvelope"/>, the body is a
    /// Cloudflare API envelope and the System-One response is its <c>result</c>; an envelope that
    /// reports failure is surfaced as an error rather than read as an empty answer set.
    /// </summary>
    private static SystemOneResponse ParseResponse(string json, bool resultEnvelope)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (resultEnvelope)
        {
            bool succeeded = root.TryGetProperty("success", out JsonElement success) &&
                success.ValueKind == JsonValueKind.True;
            if (!succeeded ||
                !root.TryGetProperty("result", out JsonElement result) ||
                result.ValueKind != JsonValueKind.Object)
            {
                string errors = root.TryGetProperty("errors", out JsonElement list) ? list.GetRawText() : "[]";
                throw new HttpRequestException($"Cloudflare reported an unsuccessful call: {errors}");
            }

            root = result;
        }

        string model = root.GetProperty("model").GetString() ?? "unknown";

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.GetProperty("answers").EnumerateObject())
        {
            JsonElement answer = property.Value;
            string type = answer.GetProperty("type").GetString()
                ?? throw new JsonException("A System-One answer is missing a type.");

            answers[property.Name] = type switch
            {
                "noul" => new NoulAnswer(answer.GetProperty("noul").GetDouble()),
                "choice" => BuildChoice(answer),
                "score" => BuildScore(answer),
                _ => throw new JsonException($"Unknown System-One answer type '{type}'."),
            };
        }

        JsonElement usage = root.GetProperty("usage");
        return new SystemOneResponse(
            model,
            answers,
            new JevUsage(
                usage.GetProperty("input_tokens").GetInt32(),
                usage.GetProperty("output_tokens").GetInt32()),
            ReadRouting(root));
    }

    private static ChoiceAnswer BuildChoice(JsonElement answer)
    {
        Dictionary<string, double> probabilities = ReadDoubleMap(answer.GetProperty("probabilities"));
        return new ChoiceAnswer(
            answer.GetProperty("choice").GetString() ?? string.Empty,
            answer.GetProperty("confidence").GetDouble(),
            ReadAnswerConfidence(answer, probabilities),
            probabilities);
    }

    private static ScoreAnswer BuildScore(JsonElement answer)
    {
        Dictionary<string, double> probabilities = ReadDoubleMap(answer.GetProperty("probabilities"));
        return new ScoreAnswer(
            answer.GetProperty("score").GetDouble(),
            answer.GetProperty("confidence").GetDouble(),
            ReadAnswerConfidence(answer, probabilities),
            ReadStringMap(answer.GetProperty("legend")),
            probabilities);
    }

    /// <summary>
    /// Laya reports <c>answer_confidence</c>; Decider reports the same provider-independent
    /// <c>max(p)</c> quantity as <c>x_p_max</c>. Jev, Clef, and Microsoft report neither, so it is recovered
    /// from the returned distribution. Clef's own <c>confidence</c> happens to equal <c>max(p)</c>,
    /// but the client does not rely on that. Policy thresholds therefore keep the same meaning on
    /// every provider.
    /// </summary>
    private static double ReadAnswerConfidence(JsonElement answer, Dictionary<string, double> probabilities)
    {
        if (answer.TryGetProperty("answer_confidence", out JsonElement layaReported) &&
            layaReported.ValueKind == JsonValueKind.Number)
        {
            return layaReported.GetDouble();
        }

        if (answer.TryGetProperty("x_p_max", out JsonElement deciderReported) &&
            deciderReported.ValueKind == JsonValueKind.Number)
        {
            return deciderReported.GetDouble();
        }

        return probabilities.Count == 0
            ? answer.GetProperty("confidence").GetDouble()
            : probabilities.Values.Max();
    }

    private static string? ReadRouting(JsonElement root)
    {
        if (!root.TryGetProperty("routing", out JsonElement routing) ||
            routing.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? checkpoint = routing.TryGetProperty("model", out JsonElement name) ? name.GetString() : null;
        string? reason = routing.TryGetProperty("reason", out JsonElement why) ? why.GetString() : null;
        if (checkpoint is null)
        {
            return null;
        }

        return reason is null ? checkpoint : $"{checkpoint} ({reason})";
    }

    private static Dictionary<string, double> ReadDoubleMap(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal);

    private static Dictionary<string, string> ReadStringMap(JsonElement element) =>
        element.EnumerateObject().ToDictionary(
            p => p.Name,
            p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.GetRawText(),
            StringComparer.Ordinal);

    private sealed record SystemOneRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("state")] object State,
        // Null is omitted by JevJson.Options. Laya then lets its router pick a checkpoint; Decider
        // uses the model selected when its server process started.
        [property: System.Text.Json.Serialization.JsonPropertyName("model")] string? Model,
        [property: System.Text.Json.Serialization.JsonPropertyName("questions")] IReadOnlyDictionary<string, JevQuestion> Questions);
}
