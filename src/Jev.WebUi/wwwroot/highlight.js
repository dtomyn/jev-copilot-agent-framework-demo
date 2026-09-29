// A deliberately small syntax highlighter. The alternative is a CDN, and this page has to work
// on a conference network that may be hostile or absent; the repository already treats offline
// operation as a property worth keeping.
//
// It is a single-pass scanner, not a parser: it knows enough about C#, JSON and PowerShell to
// make a projector-sized code listing readable, and nothing more.
(function () {
  'use strict';

  const CS_KEYWORDS = new Set((
    'abstract as async await base bool break by byte case catch char checked class const continue ' +
    'decimal default delegate do double dynamic else enum event explicit extern false finally fixed ' +
    'float for foreach from get global goto if implicit in init int interface internal is lock long ' +
    'nameof namespace new nint not null object operator or out override params partial private ' +
    'protected public readonly record ref return sbyte sealed set short sizeof stackalloc static ' +
    'string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using value ' +
    'var virtual void volatile when where while with yield'
  ).split(' '));

  const PS_KEYWORDS = new Set((
    'begin break catch continue data do dynamicparam else elseif end exit filter finally for foreach ' +
    'from function if in param process return switch throw trap try until while'
  ).split(' '));

  const SH_KEYWORDS = new Set(
    'if then else elif fi for while do done case esac function return export local set exit'.split(' ')
  );

  function escapeHtml(text) {
    return text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
  }

  function wrap(cls, text) {
    return '<span class="tok-' + cls + '">' + escapeHtml(text) + '</span>';
  }

  // Ordering matters: raw strings before verbatim before ordinary, comments before everything,
  // so that a `//` inside a string is not mistaken for a comment and the reverse.
  const CSHARP = [
    [/^\/\/[^\n]*/, 'c'],
    [/^\/\*[\s\S]*?\*\//, 'c'],
    [/^\$*"""[\s\S]*?"""/, 's'],
    [/^@"(?:[^"]|"")*"/, 's'],
    [/^\$?"(?:\\.|[^"\\\n])*"/, 's'],
    [/^'(?:\\.|[^'\\\n])'/, 's'],
    [/^\[[A-Z][\w.]*(?:\([^)\n]*\))?\]/, 'a'],
    [/^\b\d[\d_]*(?:\.\d+)?(?:[eE][-+]?\d+)?[dfmulDFMUL]*\b/, 'n'],
    [/^[A-Za-z_]\w*/, null],
    [/^\s+/, null],
    [/^[\s\S]/, null]
  ];

  const JSON_RULES = [
    [/^"(?:\\.|[^"\\])*"(?=\s*:)/, 't'],
    [/^"(?:\\.|[^"\\])*"/, 's'],
    [/^\b(?:true|false|null)\b/, 'k'],
    [/^-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?/, 'n'],
    [/^\s+/, null],
    [/^[\s\S]/, null]
  ];

  const POWERSHELL = [
    [/^<#[\s\S]*?#>/, 'c'],
    [/^#[^\n]*/, 'c'],
    [/^"(?:`.|[^"`])*"/, 's'],
    [/^'(?:''|[^'])*'/, 's'],
    [/^\$[\w:]+/, 'p'],
    [/^-[A-Za-z]\w*/, 'a'],
    [/^\b\d+\b/, 'n'],
    [/^[A-Za-z_][\w-]*/, null],
    [/^\s+/, null],
    [/^[\s\S]/, null]
  ];

  const BASH = [
    [/^#[^\n]*/, 'c'],
    [/^"(?:\\.|[^"\\])*"/, 's'],
    [/^'[^']*'/, 's'],
    [/^\$\{?[\w]+\}?/, 'p'],
    [/^--?[A-Za-z][\w-]*/, 'a'],
    [/^\b\d+\b/, 'n'],
    [/^[A-Za-z_][\w-]*/, null],
    [/^\s+/, null],
    [/^[\s\S]/, null]
  ];

  function scan(text, rules, classifyWord) {
    let out = '';
    let rest = text;
    let guard = 0;

    while (rest.length > 0 && guard++ < 400000) {
      let matched = false;
      for (const [pattern, cls] of rules) {
        const match = pattern.exec(rest);
        if (!match || match[0].length === 0) {
          continue;
        }

        const token = match[0];
        const resolved = cls || (classifyWord ? classifyWord(token, out) : null);
        out += resolved ? wrap(resolved, token) : escapeHtml(token);
        rest = rest.slice(token.length);
        matched = true;
        break;
      }

      if (!matched) {
        out += escapeHtml(rest[0]);
        rest = rest.slice(1);
      }
    }

    return out + escapeHtml(rest);
  }

  function csharpWord(token) {
    if (CS_KEYWORDS.has(token)) {
      return 'k';
    }
    // A leading capital is the convention for types and members throughout this repository, so
    // it is a good enough signal without building a symbol table.
    return /^[A-Z]/.test(token) ? 't' : null;
  }

  function highlight(text, language) {
    switch (language) {
      case 'csharp':
        return scan(text, CSHARP, csharpWord);
      case 'json':
        return scan(text, JSON_RULES, null);
      case 'powershell':
        return scan(text, POWERSHELL, t => (PS_KEYWORDS.has(t.toLowerCase()) ? 'k' : /^[A-Z][a-z]+-[A-Z]/.test(t) ? 't' : null));
      case 'bash':
        return scan(text, BASH, t => (SH_KEYWORDS.has(t) ? 'k' : null));
      default:
        return escapeHtml(text);
    }
  }

  window.JevHighlight = { highlight: highlight, escapeHtml: escapeHtml };
})();
