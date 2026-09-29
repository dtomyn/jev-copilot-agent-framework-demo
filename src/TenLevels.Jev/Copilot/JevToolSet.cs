using System.Text.Json;
using Jev.Core;
using Microsoft.Extensions.AI;

namespace TenLevels.Jev.Copilot;

public sealed class JevToolSet
{
    private readonly IJevClient _jev;

    public JevToolSet(IJevClient jev) => _jev = jev;

    public IList<AIFunctionDeclaration> AllTools() =>
    [
        AIFunctionFactory.Create(NoulAsync, "jev_noul", "Ask Jev a bounded yes/no question and receive a probability from 0 to 1."),
        AIFunctionFactory.Create(ChoiceAsync, "jev_choice", "Ask Jev to choose one item from a bounded list and receive probabilities/confidence."),
        AIFunctionFactory.Create(ScoreAsync, "jev_score", "Ask Jev to rate state against an ordered list of descriptive levels."),
    ];

    public IList<AIFunctionDeclaration> NoulOnly() =>
    [
        AIFunctionFactory.Create(NoulAsync, "jev_noul", "Ask Jev a bounded yes/no question and receive a probability from 0 to 1."),
    ];

    private async Task<string> NoulAsync(string state, string question, CancellationToken cancellationToken)
    {
        SystemOneResponse response = await _jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["answer"] = new NoulQuestion(question),
        }, cancellationToken);
        var answer = (NoulAnswer)response.Answers["answer"];
        return JsonSerializer.Serialize(new { type = "noul", probability = answer.Noul }, JevJson.Options);
    }

    private async Task<string> ChoiceAsync(string state, string question, string[] choices, CancellationToken cancellationToken)
    {
        if (choices.Length < 2)
        {
            return "{\"error\":\"Provide at least two choices.\"}";
        }
        SystemOneResponse response = await _jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["answer"] = new ChoiceQuestion(question, choices.ToDictionary(c => c, c => (string?)c, StringComparer.Ordinal)),
        }, cancellationToken);
        var answer = (ChoiceAnswer)response.Answers["answer"];
        return JsonSerializer.Serialize(new
        {
            type = "choice",
            choice = answer.Choice,
            confidence = answer.Confidence,
            probabilities = answer.Probabilities,
        }, JevJson.Options);
    }

    private async Task<string> ScoreAsync(string state, string question, string[] levels, CancellationToken cancellationToken)
    {
        if (levels.Length < 2)
        {
            return "{\"error\":\"Provide at least two ordered levels.\"}";
        }
        SystemOneResponse response = await _jev.DecideAsync(state, new Dictionary<string, JevQuestion>
        {
            ["answer"] = new ScoreQuestion(question, levels),
        }, cancellationToken);
        var answer = (ScoreAnswer)response.Answers["answer"];
        return JsonSerializer.Serialize(new
        {
            type = "score",
            score = answer.Score,
            confidence = answer.Confidence,
            legend = answer.Legend,
            probabilities = answer.Probabilities,
        }, JevJson.Options);
    }
}
