namespace Jev.Core;

public sealed class MockJevClient : IJevClient
{
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
            "mock-jev",
            answers,
            new JevUsage(Math.Max(1, text.Length / 4), questions.Count * 4)));
    }

    private static NoulAnswer BuildNoul(RiskBand risk) => risk switch
    {
        RiskBand.Low => new NoulAnswer(0.03),
        RiskBand.Medium => new NoulAnswer(0.58),
        RiskBand.High => new NoulAnswer(0.94),
        _ => new NoulAnswer(0.5),
    };

    private static ChoiceAnswer BuildChoice(ChoiceQuestion question, RiskBand risk, string text)
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
            double confidence = risk == RiskBand.Medium ? 0.76 : 0.94;
            return new ChoiceAnswer(chosen, confidence, Distribution(keys, chosen, confidence));
        }

        string matched = keys.FirstOrDefault(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)) ?? keys[0];
        return new ChoiceAnswer(matched, 0.88, Distribution(keys, matched, 0.88));
    }

    private static ScoreAnswer BuildScore(ScoreQuestion question, RiskBand risk)
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
        var probabilities = question.Criteria
            .Select((_, index) => new KeyValuePair<string, double>(
                index.ToString(),
                index == nearest ? 0.88 : 0.12 / Math.Max(1, question.Criteria.Count - 1)))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        return new ScoreAnswer(
            Math.Round(score, 2),
            risk == RiskBand.Medium ? 0.73 : 0.91,
            legend,
            probabilities);
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
