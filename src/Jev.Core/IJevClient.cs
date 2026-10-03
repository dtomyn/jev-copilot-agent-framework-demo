namespace Jev.Core;

/// <summary>
/// A System-One decision provider: TypeSafe Jev, Laya, Decider, Clef, or the offline mock.
/// </summary>
public interface IJevClient
{
    /// <summary>
    /// Which provider family this client represents. Callers use it for naming (the Agent
    /// Framework function tools) and for wording, never to change the meaning of an answer: the
    /// DTOs are normalized so policy code does not branch per provider.
    /// </summary>
    DecisionProvider Provider { get; }

    /// <summary>One line naming the provider and mode, for demo and log output.</summary>
    string Description { get; }

    Task<SystemOneResponse> DecideAsync(
        object state,
        IReadOnlyDictionary<string, JevQuestion> questions,
        CancellationToken cancellationToken = default);
}
