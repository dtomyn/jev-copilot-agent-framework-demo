using System.Text.Json.Serialization;

namespace Jev.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract record JevQuestion;

public sealed record NoulQuestion(
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] NoulCriteria? Criteria = null) : JevQuestion;

public sealed record NoulCriteria(
    [property: JsonPropertyName("true")] string? WhenTrue = null,
    [property: JsonPropertyName("false")] string? WhenFalse = null);

public sealed record ChoiceQuestion(
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyDictionary<string, string?> Criteria) : JevQuestion;

public sealed record ScoreQuestion(
    [property: JsonPropertyName("instructions")] string Instructions,
    [property: JsonPropertyName("criteria")] IReadOnlyList<string> Criteria) : JevQuestion;

public abstract record JevAnswer;

public sealed record NoulAnswer(double Noul) : JevAnswer
{
    /// <summary>
    /// Probability mass on the reported answer. Over two options that is
    /// <c>max(p_yes, p_no)</c>, so it is derived rather than read off the wire.
    /// </summary>
    public double AnswerConfidence => Math.Max(Noul, 1 - Noul);
}

/// <param name="Confidence">
/// The provider's own confidence number, reported as sent. It is <b>not</b> comparable across
/// providers: Jev defines it as <c>(n*p_max - 1)/(n - 1)</c> and Laya as the normalized entropy
/// <c>1 - H(p)/log(k)</c>. Use it for display, not for a threshold that has to hold on both.
/// </param>
/// <param name="AnswerConfidence">
/// Probability mass on the reported answer (<c>max(p)</c>). Provider-independent by construction,
/// which is why <see cref="CopilotToolGate"/> gates on this and not on <paramref name="Confidence"/>.
/// </param>
public sealed record ChoiceAnswer(
    string Choice,
    double Confidence,
    double AnswerConfidence,
    IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;

/// <param name="Confidence">Provider-specific; see <see cref="ChoiceAnswer.Confidence"/>.</param>
/// <param name="AnswerConfidence">
/// Probability mass on the most likely level (<c>max(p)</c>), not on the expected
/// <paramref name="Score"/>, which may fall between levels.
/// </param>
public sealed record ScoreAnswer(
    double Score,
    double Confidence,
    double AnswerConfidence,
    IReadOnlyDictionary<string, string> Legend,
    IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;

public sealed record JevUsage(int InputTokens, int OutputTokens);

/// <param name="Model">The decision head that answered, as named by the provider.</param>
/// <param name="Routing">
/// Which checkpoint the provider's router picked and why, when it reports one. Laya fills this in;
/// Jev has no equivalent field and leaves it <c>null</c>.
/// </param>
public sealed record SystemOneResponse(
    string Model,
    IReadOnlyDictionary<string, JevAnswer> Answers,
    JevUsage Usage,
    string? Routing = null);
