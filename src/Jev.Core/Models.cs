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

public sealed record NoulAnswer(double Noul) : JevAnswer;

public sealed record ChoiceAnswer(
    string Choice,
    double Confidence,
    IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;

public sealed record ScoreAnswer(
    double Score,
    double Confidence,
    IReadOnlyDictionary<string, string> Legend,
    IReadOnlyDictionary<string, double> Probabilities) : JevAnswer;

public sealed record JevUsage(int InputTokens, int OutputTokens);

public sealed record SystemOneResponse(
    string Model,
    IReadOnlyDictionary<string, JevAnswer> Answers,
    JevUsage Usage);
