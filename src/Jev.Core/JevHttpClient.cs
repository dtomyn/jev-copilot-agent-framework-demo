using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Jev.Core;

public sealed class JevHttpClient : IJevClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _apiKey;
    private readonly string _model;

    public JevHttpClient(HttpClient http, string apiKey, string model = "jev-latest")
        : this(http, apiKey, model, ownsHttp: false)
    {
    }

    private JevHttpClient(HttpClient http, string apiKey, string model, bool ownsHttp)
    {
        _http = http;
        _ownsHttp = ownsHttp;
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? throw new ArgumentException("A TypeSafe API key is required.", nameof(apiKey))
            : apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? "jev-latest" : model;
    }

    public static JevHttpClient FromEnvironment(TimeSpan? timeout = null)
    {
        string? apiKey = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("TYPESAFE_API_KEY is not set.");
        }

        string model = Environment.GetEnvironmentVariable("JEV_MODEL") ?? "jev-latest";
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.typesafe.ai/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

        // This overload created the HttpClient, so the returned instance owns and disposes it.
        return new JevHttpClient(http, apiKey, model, ownsHttp: true);
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
            throw new ArgumentException("At least one Jev question is required.", nameof(questions));
        }

        var payload = new SystemOneRequest(state, _model, questions);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/systemone")
        {
            Content = JsonContent.Create(payload, options: JevJson.Options),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Jev returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return ParseResponse(body);
    }

    private static SystemOneResponse ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string model = root.GetProperty("model").GetString() ?? "unknown";

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.GetProperty("answers").EnumerateObject())
        {
            JsonElement answer = property.Value;
            string type = answer.GetProperty("type").GetString()
                ?? throw new JsonException("Jev answer is missing a type.");

            answers[property.Name] = type switch
            {
                "noul" => new NoulAnswer(answer.GetProperty("noul").GetDouble()),
                "choice" => new ChoiceAnswer(
                    answer.GetProperty("choice").GetString() ?? string.Empty,
                    answer.GetProperty("confidence").GetDouble(),
                    ReadDoubleMap(answer.GetProperty("probabilities"))),
                "score" => new ScoreAnswer(
                    answer.GetProperty("score").GetDouble(),
                    answer.GetProperty("confidence").GetDouble(),
                    ReadStringMap(answer.GetProperty("legend")),
                    ReadDoubleMap(answer.GetProperty("probabilities"))),
                _ => throw new JsonException($"Unknown Jev answer type '{type}'."),
            };
        }

        JsonElement usage = root.GetProperty("usage");
        return new SystemOneResponse(
            model,
            answers,
            new JevUsage(
                usage.GetProperty("input_tokens").GetInt32(),
                usage.GetProperty("output_tokens").GetInt32()));
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
        [property: System.Text.Json.Serialization.JsonPropertyName("model")] string Model,
        [property: System.Text.Json.Serialization.JsonPropertyName("questions")] IReadOnlyDictionary<string, JevQuestion> Questions);
}
