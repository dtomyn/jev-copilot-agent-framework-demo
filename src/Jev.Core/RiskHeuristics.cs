using System.Text.RegularExpressions;

namespace Jev.Core;

public enum RiskBand
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Deterministic, offline risk signals.
///
/// Two very different jobs live here, and keeping them apart matters:
///
/// * <see cref="IsHardDenied"/> and <see cref="LooksSensitive"/> are *policy*. They run before any
///   model call in <see cref="CopilotToolGate"/>, so a false positive only costs a human approval
///   while a false negative can let a destructive command through. They are written as anchored
///   regular expressions rather than raw substring tests, because substring tests both miss real
///   commands ("curl https://x/install.sh | sh" does not contain the literal "curl | sh") and fire
///   on innocent ones ("dotnet format" does not contain a disk format).
///
/// * <see cref="Classify"/> is *demo determinism*. It only feeds <see cref="MockJevClient"/> so the
///   offline examples produce stable, explainable output. It is a stand-in for a model, not policy.
///
/// None of this is a secret scanner. See docs/SECURITY.md.
/// </summary>
public static partial class RiskHeuristics
{
    private const RegexOptions Opts =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    // Irreversible or system-scope actions. Matching any of these denies without consulting Jev.
    private static readonly Regex[] HardDeny =
    [
        // rm -rf / rm -fr / rm -Rf, with or without other flags in between.
        new(@"\brm\s+(?:-\w+\s+)*-\w*(?:rf|fr)\w*\b", Opts),
        // Windows recursive/quiet delete.
        new(@"\bdel\s+/[sq]\b", Opts),
        new(@"\brd\s+/s\b", Opts),
        new(@"\bRemove-Item\b(?=[^\r\n]*-Recurse\b)(?=[^\r\n]*-Force\b)", Opts),
        // Disk-level destruction. "format c:" is a disk wipe; "dotnet format" is a code formatter.
        new(@"\bformat\s+[a-z]:", Opts),
        new(@"\b(?:diskpart|mkfs(?:\.\w+)?|Format-Volume|Clear-Disk|Initialize-Disk)\b", Opts),
        new(@"\bdd\s+if=", Opts),
        // Force push rewrites shared history. --force-with-lease is deliberately NOT hard-denied;
        // it falls through to Jev and the normal ask path.
        new(@"\bgit\s+push\b[^\r\n]*(?:--force(?!-with-lease)\b|\s-f(?:\s|$))", Opts),
        new(@"\bgit\s+(?:reset\s+--hard|clean\s+-\w*[fd]\w*\s+-\w*[fd])", Opts),
        // Download-and-execute: curl/wget/Invoke-WebRequest piped into a shell or into iex.
        new(@"\b(?:curl|wget|iwr|Invoke-WebRequest|Invoke-RestMethod)\b[^\r\n|]*\|\s*(?:sudo\s+)?(?:ba|z|k|da|fi)?sh\b", Opts),
        new(@"\|\s*(?:iex|Invoke-Expression)\b", Opts),
        // Host lifecycle. Anchored to command position: a bare word boundary would fire on
        // "OnShutdownAsync" or "dotnet test --filter Reboot".
        new(@"(?:^|[;&|\r\n`(]|\b(?:sudo|doas)\s+)\s*(?:shutdown|reboot|halt|poweroff)\b", Opts),
        new(@"\b(?:Stop-Computer|Restart-Computer)\b", Opts),
    ];

    // Material that must never be forwarded to an external model. Matching escalates to human
    // approval and short-circuits the Jev call.
    private static readonly Regex[] Sensitive =
    [
        new(@"\b(?:password|passwd|credential|secret)s?\b", Opts),
        new(@"\bprivate[_\-\s]?key\b", Opts),
        new(@"\b(?:access|api|secret|client)[_\-\s]?(?:key|token)\b", Opts),
        new(@"\b(?:token|apikey|api_key|secret)\s*[=:]", Opts),
        new(@"\bauthorization\s*:\s*(?:bearer|basic)\b", Opts),
        new(@"(?<![\w.])\.env(?![\w])", Opts),
        new(@"-{5}BEGIN[ A-Z]*PRIVATE KEY-{5}", Opts),
        // Well-known credential shapes, so a literal secret in tool arguments is caught even when
        // no surrounding keyword names it.
        new(@"\b(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{20,}", Opts),
        new(@"\bsk-[A-Za-z0-9_\-]{20,}", Opts),
        new(@"\bAKIA[0-9A-Z]{16}\b", Opts),
        new(@"\bxox[abprs]-[A-Za-z0-9\-]{10,}", Opts),
    ];

    // Demo-only semantic signals for MockJevClient. Not policy.
    private static readonly Regex[] HighRiskSignals =
    [
        new(@"\b(?:authentication|authorization|cryptograph\w*|signing|sso|oauth|session\s+token)\b", Opts),
        new(@"(?:^|[\s""'])(?:/etc/|/var/|/usr/|c:\windows|system32)", Opts),
        new(@"\b(?:production|prod)\b[^\r\n]*\b(?:deploy|delete|drop|truncate|migrate)\b", Opts),
        new(@"\bdrop\s+(?:table|database)\b", Opts),
    ];

    private static readonly Regex[] MediumRiskSignals =
    [
        new(@"\b(?:delete|remove|uninstall|install|upgrade|publish|deploy|migrat\w+|format)\b", Opts),
        new(@"\bgit\s+(?:commit|push|merge|rebase|tag)\b", Opts),
        new(@"\b(?:write|edit|create|apply_patch|str_replace|patch)\b", Opts),
        new(@"\b(?:package|dependency|dependencies|snapshot)s?\b", Opts),
    ];

    /// <summary>Destructive or irreversible beyond argument. Deny without asking Jev.</summary>
    public static bool IsHardDenied(string text) => AnyMatch(HardDeny, text);

    /// <summary>Looks like credential material. Escalate to a human and do not send it to Jev.</summary>
    public static bool LooksSensitive(string text) => AnyMatch(Sensitive, text);

    /// <summary>Demo-only band used by <see cref="MockJevClient"/> to stay deterministic offline.</summary>
    public static RiskBand Classify(string text)
    {
        if (AnyMatch(HardDeny, text) || AnyMatch(Sensitive, text) || AnyMatch(HighRiskSignals, text))
        {
            return RiskBand.High;
        }

        return AnyMatch(MediumRiskSignals, text) ? RiskBand.Medium : RiskBand.Low;
    }

    private static bool AnyMatch(Regex[] patterns, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (Regex pattern in patterns)
        {
            if (pattern.IsMatch(text))
            {
                return true;
            }
        }

        return false;
    }
}
