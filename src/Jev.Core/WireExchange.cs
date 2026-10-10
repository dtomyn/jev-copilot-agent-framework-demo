namespace Jev.Core;

/// <summary>One HTTP header as it went over the wire. Credentials are already masked.</summary>
public sealed record WireHeader(string Name, string Value);

/// <summary>
/// Exactly what one System-One call put on the wire: the resolved URL, the request headers and body,
/// and whatever came back, including a failure. Surfaced by the web UI's diagnostics area. Credential
/// header values are masked here, at the source, so no consumer can leak one by forgetting to.
/// </summary>
/// <param name="StatusCode">HTTP status, or <c>null</c> when no response arrived (timeout, TLS, refused).</param>
/// <param name="Error">Why the call failed before or instead of a response, otherwise <c>null</c>.</param>
public sealed record WireExchange(
    string Method,
    string Url,
    IReadOnlyList<WireHeader> RequestHeaders,
    string RequestBody,
    int? StatusCode,
    string? ReasonPhrase,
    IReadOnlyList<WireHeader> ResponseHeaders,
    string? ResponseBody,
    long ElapsedMs,
    string? Error);
