using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Jev.Core;

/// <summary>
/// One client for the <c>POST /v1/systemone</c> System-One contract shared by TypeSafe Jev,
/// Laya, Mapika Decider, and Cloudflare Clef. Everything provider-specific is in the
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
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.RequestPath)
        {
            Content = JsonContent.Create(payload, options: JevJson.Options),
        };

        // Local providers normally use no bearer token: Laya and local Clef can opt into one, and
        // Decider's stock server is unauthenticated. Hosted Clef always sends a Cloudflare API
        // token. Never send an empty Authorization header.
        if (_endpoint.ApiKey is { Length: > 0 } apiKey)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"{_endpoint.DisplayName} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return ParseResponse(body, _endpoint.ResultEnvelope);
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
    /// <c>max(p)</c> quantity as <c>x_p_max</c>. Jev and Clef report neither, so it is recovered
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
