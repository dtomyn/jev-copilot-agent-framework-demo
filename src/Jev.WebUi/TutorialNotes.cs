using System.Net;
using System.Text.RegularExpressions;

namespace Jev.WebUi;

/// <summary>
/// Pulls the presenter notes out of <c>docs/tutorial.html</c> and indexes them by level.
///
/// The notes are written once, in the tutorial, and read here. The alternative - a second copy of
/// the same paragraphs inside this project - is the kind of duplication that survives exactly
/// until the first time someone improves the wording in one place.
///
/// The coupling is explicit rather than inferred: a notes block appears in the UI only if it
/// carries <c>data-level</c>. Blocks about bootstrapping or Docker have no level and are skipped,
/// and a new note joins a level by gaining one attribute.
/// </summary>
public sealed partial class TutorialNotes
{
    public const string TutorialPath = "docs/tutorial.html";

    private readonly RepositoryContent _repository;
    private readonly Lazy<IReadOnlyDictionary<int, IReadOnlyList<TutorialNote>>> _byLevel;

    public TutorialNotes(RepositoryContent repository)
    {
        _repository = repository;
        _byLevel = new Lazy<IReadOnlyDictionary<int, IReadOnlyList<TutorialNote>>>(Load);
    }

    public bool Available => _repository.Read(TutorialPath) is not null;

    public IReadOnlyList<TutorialNote> For(int level) =>
        _byLevel.Value.TryGetValue(level, out IReadOnlyList<TutorialNote>? notes) ? notes : [];

    private Dictionary<int, IReadOnlyList<TutorialNote>> Load()
    {
        var index = new Dictionary<int, List<TutorialNote>>();
        if (_repository.Read(TutorialPath) is not { } html)
        {
            return index.ToDictionary(p => p.Key, p => (IReadOnlyList<TutorialNote>)p.Value);
        }

        // Headings are scanned first so each notes block can be attributed to the section it sits
        // in: the nearest heading for a label, the nearest heading that has an id for a link.
        List<(int Offset, string Text, string? Id)> headings = [.. HeadingPattern()
            .Matches(html)
            .Select(m => (m.Index, StripTags(m.Groups["text"].Value), IdOf(m.Groups["open"].Value)))];

        foreach (Match match in NotesOpenPattern().Matches(html))
        {
            string? levels = AttributePattern().Match(match.Value) is { Success: true } attribute
                ? attribute.Groups[1].Value
                : null;
            if (levels is null)
            {
                continue;
            }

            int start = match.Index + match.Length;
            int end = FindCloseTag(html, start);
            if (end < 0)
            {
                continue;
            }

            string body = Sanitize(html[start..end]);
            (string heading, string? anchor) = SectionOf(headings, match.Index);
            var note = new TutorialNote(heading, anchor, body);

            foreach (string token in levels.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(token, out int level))
                {
                    if (!index.TryGetValue(level, out List<TutorialNote>? bucket))
                    {
                        bucket = [];
                        index[level] = bucket;
                    }

                    bucket.Add(note);
                }
            }
        }

        return index.ToDictionary(p => p.Key, p => (IReadOnlyList<TutorialNote>)p.Value);
    }

    private static (string Heading, string? Anchor) SectionOf(
        List<(int Offset, string Text, string? Id)> headings,
        int offset)
    {
        string heading = "Tutorial";
        string? anchor = null;
        foreach ((int position, string text, string? id) in headings)
        {
            if (position > offset)
            {
                break;
            }

            heading = text;
            if (id is not null)
            {
                anchor = id;
            }
        }

        return (heading, anchor);
    }

    /// <summary>
    /// Finds the <c>&lt;/div&gt;</c> that closes the notes block, counting nested divs: every
    /// block opens with a <c>&lt;div class="label"&gt;</c> of its own.
    /// </summary>
    private static int FindCloseTag(string html, int start)
    {
        int depth = 1;
        int cursor = start;
        while (cursor < html.Length)
        {
            int open = html.IndexOf("<div", cursor, StringComparison.OrdinalIgnoreCase);
            int close = html.IndexOf("</div>", cursor, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
            {
                return -1;
            }

            if (open >= 0 && open < close)
            {
                depth++;
                cursor = open + 4;
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return close;
            }

            cursor = close + 6;
        }

        return -1;
    }

    /// <summary>
    /// Drops the block's own label, plus anything executable. The source is a file checked into
    /// this repository rather than user input, so this is a guard against a future edit pasting
    /// something surprising into the tutorial, not a sanitizer anyone should rely on.
    /// </summary>
    private static string Sanitize(string fragment)
    {
        string cleaned = LabelPattern().Replace(fragment, string.Empty);
        cleaned = ScriptOrStylePattern().Replace(cleaned, string.Empty);
        cleaned = EventAttributePattern().Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }

    /// <summary>
    /// A heading's text, without the chrome the tutorial puts inside its headings: the step
    /// number badge, which would otherwise run into the first word, and the "Run live" link,
    /// which points back at this application and would read as part of the title.
    /// </summary>
    private static string StripTags(string fragment)
    {
        string text = HeadingChromePattern().Replace(fragment, string.Empty);
        text = TagPattern().Replace(text, string.Empty);
        return WhitespacePattern().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    private static string? IdOf(string openTag) =>
        IdPattern().Match(openTag) is { Success: true } match ? match.Groups[1].Value : null;

    [GeneratedRegex(@"(?<open><h[23][^>]*>)(?<text>.*?)</h[23]>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"<div\s+class=""notes""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex NotesOpenPattern();

    [GeneratedRegex(@"data-level=""([^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex AttributePattern();

    [GeneratedRegex(@"id=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"<div\s+class=""label"">.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStylePattern();

    [GeneratedRegex(@"\son[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttributePattern();

    [GeneratedRegex(@"<(?<tag>span|a)\s+class=""(?:stepnum|runlive)""[^>]*>.*?</\k<tag>>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingChromePattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}

/// <param name="Section">The tutorial heading this note sits under.</param>
/// <param name="Anchor">Fragment id of the nearest linkable heading, or <c>null</c>.</param>
/// <param name="Html">The note's body, with its "Presenter note" label removed.</param>
public sealed record TutorialNote(string Section, string? Anchor, string Html);
