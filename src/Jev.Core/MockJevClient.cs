namespace Jev.Core;

/// <summary>
/// Deterministic offline stand-in for a System-One provider, so every level and every hook sample
/// produces stable, explainable output with no network and no API key.
///
/// It also reproduces the provider-specific meaning of <c>confidence</c>. The probabilities are
/// identical across providers, but the reported
/// <see cref="ChoiceAnswer.Confidence"/> follows whichever formula the selected provider uses. The gate is unaffected because it reads
/// <c>AnswerConfidence</c>, and a self-test pins that.
/// </summary>
public sealed class MockJevClient : IJevClient
{
    public MockJevClient(DecisionProvider provider = DecisionProvider.Jev) => Provider = provider;

    public DecisionProvider Provider { get; }

    public string Description => $"{SystemOneEndpoint.ProviderDisplayName(Provider)} mock (offline, deterministic)";

    public Task<SystemOneResponse> DecideAsync(
        object state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        string text = state.ToString() ?? string.Empty;
        RiskBand risk = RiskHeuristics.Classify(text);
        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach ((string name, JevQuestion question) in questions)
        {
            answers[name] = question switch
            {
                NoulQuestion => BuildNoul(risk),
                ChoiceQuestion choice => BuildChoice(choice, risk, text),
                ScoreQuestion score => BuildScore(score, risk),
                _ => throw new NotSupportedException($"Unsupported question type {question.GetType().Name}."),
            };
        }

        return Task.FromResult(new SystemOneResponse(
            $"mock-{SystemOneEndpoint.ProviderId(Provider)}",
            answers,
            new JevUsage(Math.Max(1, text.Length / 4), questions.Count * 4),
            Provider == DecisionProvider.Laya ? "mock (no checkpoint loaded)" : null));
    }

    private static NoulAnswer BuildNoul(RiskBand risk) => risk switch
    {
        RiskBand.Low => new NoulAnswer(0.03),
        RiskBand.Medium => new NoulAnswer(0.58),
        RiskBand.High => new NoulAnswer(0.94),
        _ => new NoulAnswer(0.5),
    };

    private ChoiceAnswer BuildChoice(ChoiceQuestion question, RiskBand risk, string text)
    {
        string[] keys = question.Criteria.Keys.ToArray();
        if (keys.Length == 0)
        {
            throw new InvalidOperationException("A choice question needs at least one choice.");
        }

        if (keys.Contains("allow", StringComparer.OrdinalIgnoreCase) &&
            keys.Contains("ask", StringComparer.OrdinalIgnoreCase) &&
            keys.Contains("deny", StringComparer.OrdinalIgnoreCase))
        {
            string chosen = risk switch
            {
                RiskBand.Low => Find(keys, "allow"),
                RiskBand.Medium => Find(keys, "ask"),
                RiskBand.High => Find(keys, "deny"),
                _ => Find(keys, "ask"),
            };
            double selected = risk == RiskBand.Medium ? 0.76 : 0.94;
            return BuildChoiceAnswer(keys, chosen, selected);
        }

        string matched = keys.FirstOrDefault(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? keys[0];
        return BuildChoiceAnswer(keys, matched, 0.88);
    }

    private ChoiceAnswer BuildChoiceAnswer(string[] keys, string chosen, double selected)
    {
        Dictionary<string, double> probabilities = Distribution(keys, chosen, selected);
        return new ChoiceAnswer(
            chosen,
            ProviderConfidence(probabilities.Values, isScore: false),
            selected,
            probabilities);
    }

    private ScoreAnswer BuildScore(ScoreQuestion question, RiskBand risk)
    {
        if (question.Criteria.Count == 0)
        {
            throw new InvalidOperationException("A score question needs at least one level.");
        }

        double normalized = risk switch
        {
            RiskBand.Low => 0.05,
            RiskBand.Medium => 0.52,
            RiskBand.High => 0.95,
            _ => 0.5,
        };
        double score = normalized * Math.Max(0, question.Criteria.Count - 1);
        int nearest = (int)Math.Round(score, MidpointRounding.AwayFromZero);
        nearest = Math.Clamp(nearest, 0, question.Criteria.Count - 1);

        var legend = question.Criteria
            .Select((value, index) => new KeyValuePair<string, string>(index.ToString(), value))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        const double Selected = 0.88;
        var probabilities = question.Criteria
            .Select((_, index) => new KeyValuePair<string, double>(
                index.ToString(),
                index == nearest ? Selected : (1 - Selected) / Math.Max(1, question.Criteria.Count - 1)))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return new ScoreAnswer(
            Math.Round(score, 2),
            ProviderConfidence(probabilities.Values, isScore: true),
            Selected,
            legend,
            probabilities);
    }

    /// <summary>
    /// Reproduces the selected provider's own display <c>confidence</c>. Jev and Decider Choice
    /// use <c>(n*p_max - 1)/(n - 1)</c>; Decider Score uses TypeSafe's distance formula; Laya uses
    /// normalized entropy; Clef reports <c>max(p)</c> itself. Policy deliberately thresholds the
    /// separate max(p) value instead.
    /// </summary>
    private double ProviderConfidence(IEnumerable<double> probabilities, bool isScore)
    {
        double[] p = probabilities.ToArray();
        if (p.Length < 2)
        {
            return 1.0;
        }

        if (Provider == DecisionProvider.Clef)
        {
            return Math.Round(p.Max(), 4);
        }

        if (Provider == DecisionProvider.Laya)
        {
            double entropy = p.Where(v => v > 0).Sum(v => -v * Math.Log(v));
            return Math.Round(1 - (entropy / Math.Log(p.Length)), 4);
        }

        if (Provider == DecisionProvider.Decider && isScore)
        {
            int mostLikely = Array.IndexOf(p, p.Max());
            double middle = (p.Length - 1) / 2.0;
            double denominator = Enumerable.Range(0, p.Length).Average(i => Math.Abs(i - middle));
            double expectedDistance = p.Select((value, i) => value * Math.Abs(i - mostLikely)).Sum();
            return Math.Round(Math.Max(0, 1 - (expectedDistance / denominator)), 4);
        }

        return Math.Round(((p.Length * p.Max()) - 1) / (p.Length - 1), 4);
    }

    private static Dictionary<string, double> Distribution(string[] keys, string chosen, double selected)
    {
        double remainder = keys.Length == 1 ? 0 : (1 - selected) / (keys.Length - 1);
        return keys.ToDictionary(
            k => k,
            k => string.Equals(k, chosen, StringComparison.Ordinal) ? selected : remainder,
            StringComparer.Ordinal);
    }

    private static string Find(IEnumerable<string> keys, string value) =>
        keys.First(k => string.Equals(k, value, StringComparison.OrdinalIgnoreCase));
}
