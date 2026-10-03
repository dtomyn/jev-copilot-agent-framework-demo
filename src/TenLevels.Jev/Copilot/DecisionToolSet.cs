using System.Text.Json;
using Jev.Core;
using Microsoft.Extensions.AI;

namespace TenLevels.Jev.Copilot;

/// <summary>
/// The three System-One primitives, exposed to a Microsoft Agent Framework agent as function
/// tools. The tool names carry the provider prefix (<c>jev_*</c>, <c>laya_*</c>,
/// <c>decider_*</c>, or <c>clef_*</c>) so a transcript shows which engine answered; <c>CopilotToolGate</c> allows
/// all provider prefixes, because the
/// repository hook also sees these calls.
/// </summary>
public sealed class DecisionToolSet
{
    private readonly IJevClient _jev;
    private readonly string _prefix;
    private readonly string _providerName;

    public DecisionToolSet(IJevClient jev)
    {
        _jev = jev;
        _prefix = SystemOneEndpoint.ProviderId(jev.Provider);
        _providerName = SystemOneEndpoint.ProviderDisplayName(jev.Provider);
    }

    public string NoulToolName => $"{_prefix}_noul";

    public string ChoiceToolName => $"{_prefix}_choice";

    public string ScoreToolName => $"{_prefix}_score";

    public IList<AIFunctionDeclaration> AllTools() =>
    [
        AIFunctionFactory.Create(NoulAsync, NoulToolName, $"Ask {_providerName} a bounded yes/no question and receive a probability from 0 to 1."),
        AIFunctionFactory.Create(ChoiceAsync, ChoiceToolName, $"Ask {_providerName} to choose one item from a bounded list and receive probabilities/confidence."),
        AIFunctionFactory.Create(ScoreAsync, ScoreToolName, $"Ask {_providerName} to rate state against an ordered list of descriptive levels."),
    ];

    public IList<AIFunctionDeclaration> NoulOnly() =>
    [
        AIFunctionFactory.Create(NoulAsync, NoulToolName, $"Ask {_providerName} a bounded yes/no question and receive a probability from 0 to 1."),
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
            // Both numbers, named apart: `confidence` is the provider's own definition and is not
            // comparable across providers, `answerConfidence` is max(p) and is.
            confidence = answer.Confidence,
            answerConfidence = answer.AnswerConfidence,
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
            answerConfidence = answer.AnswerConfidence,
            legend = answer.Legend,
            probabilities = answer.Probabilities,
        }, JevJson.Options);
    }
}
