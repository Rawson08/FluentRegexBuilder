using System.Text;
using System.Text.RegularExpressions;

namespace FluentRegexBuilder;

/// <summary>
/// A fluent, readable regex builder for C#.
/// Every method appends a piece of pattern; call ToRegex() (or Pattern) at the end.
///
/// Design notes:
///  - "Atoms" (literals, character classes, groups, shorthands) remember their start
///    position so quantifiers (Exactly, OneOrMore, ...) can wrap them safely.
///  - Literals passed to Then/Maybe/OneOf are escaped automatically — you never
///    need to worry about '.', '(', '+' etc. having special meaning.
///  - Set(...) is the escape hatch: raw character-class content, not escaped.
///  - Alternation (Or) binds loosely, exactly like '|' in raw regex. Wrap the
///    alternatives in Group/NonCaptureGroup if you need tighter scope.
/// </summary>
public sealed class FluentRegex
{
    private readonly StringBuilder _pattern = new();
    private RegexOptions _options = RegexOptions.None;
    private int _lastAtomStart = -1;

    private FluentRegex() { }

    /// <summary>Start building a new pattern.</summary>
    public static FluentRegex Create() => new();

    // =====================================================================
    // Anchors — positions, not characters
    // =====================================================================

    /// <summary>^ — start of line (or string).</summary>
    public FluentRegex StartOfLine() => AppendRaw("^");

    /// <summary>$ — end of line (or string).</summary>
    public FluentRegex EndOfLine() => AppendRaw("$");

    /// <summary>\A — start of the entire string, even in multiline mode.</summary>
    public FluentRegex StartOfString() => AppendRaw(@"\A");

    /// <summary>\z — absolute end of the entire string.</summary>
    public FluentRegex EndOfString() => AppendRaw(@"\z");

    /// <summary>\b — word boundary (edge between \w and non-\w).</summary>
    public FluentRegex WordBoundary() => AppendRaw(@"\b");

    /// <summary>\B — NOT a word boundary.</summary>
    public FluentRegex NonWordBoundary() => AppendRaw(@"\B");

    // =====================================================================
    // Literals
    // =====================================================================

    /// <summary>Match this exact text. Special characters are escaped for you.</summary>
    public FluentRegex Then(string literal) => AppendAtom(Regex.Escape(literal));

    /// <summary>Alias for Then — reads better at the start of a chain.</summary>
    public FluentRegex Find(string literal) => Then(literal);

    /// <summary>Optionally match this exact text: (?:literal)?</summary>
    public FluentRegex Maybe(string literal) =>
        AppendAtom("(?:" + Regex.Escape(literal) + ")?");

    // =====================================================================
    // Character classes & shorthands
    // =====================================================================

    /// <summary>\d — any digit 0–9.</summary>
    public FluentRegex Digit() => AppendAtom(@"\d");

    /// <summary>\D — any character that is NOT a digit.</summary>
    public FluentRegex NonDigit() => AppendAtom(@"\D");

    /// <summary>\w — letter, digit, or underscore.</summary>
    public FluentRegex WordChar() => AppendAtom(@"\w");

    /// <summary>\W — anything that is NOT a word character.</summary>
    public FluentRegex NonWordChar() => AppendAtom(@"\W");

    /// <summary>\s — any whitespace (space, tab, newline...).</summary>
    public FluentRegex Whitespace() => AppendAtom(@"\s");

    /// <summary>\S — anything that is NOT whitespace.</summary>
    public FluentRegex NonWhitespace() => AppendAtom(@"\S");

    /// <summary>. — any single character (except newline unless Singleline option).</summary>
    public FluentRegex AnyChar() => AppendAtom(".");

    /// <summary>\t — a tab character.</summary>
    public FluentRegex Tab() => AppendAtom(@"\t");

    /// <summary>Matches \r\n, \r, or \n.</summary>
    public FluentRegex LineBreak() => AppendAtom(@"(?:\r\n|\r|\n)");

    /// <summary>[chars] — any ONE of these characters. Escaped for you.</summary>
    public FluentRegex AnyOf(string chars) =>
        AppendAtom("[" + EscapeForClass(RequireChars(chars)) + "]");

    /// <summary>[^chars] — any ONE character NOT in this set. Escaped for you.</summary>
    public FluentRegex NoneOf(string chars) =>
        AppendAtom("[^" + EscapeForClass(RequireChars(chars)) + "]");

    /// <summary>[from-to] — one character in this range, e.g. Range('A','Z'). Escaped for you.</summary>
    public FluentRegex Range(char from, char to) =>
        AppendAtom("[" + EscapeCharForClass(from) + "-" + EscapeCharForClass(to) + "]");

    /// <summary>
    /// Raw character-class content — NOT escaped. The power-user escape hatch.
    /// Set("A-Za-z0-9") → [A-Za-z0-9]. Set("0-9", negate: true) → [^0-9].
    /// </summary>
    public FluentRegex Set(string classContent, bool negate = false) =>
        AppendAtom("[" + (negate ? "^" : "") + classContent + "]");

    /// <summary>[^chars]* — zero or more characters, none of which are in the set.
    /// (Classic VerbalExpressions behavior — already quantified, don't add OneOrMore.)</summary>
    public FluentRegex AnythingBut(string chars) =>
        AppendAtom("[^" + EscapeForClass(RequireChars(chars)) + "]*");

    /// <summary>.* — zero or more of anything.</summary>
    public FluentRegex Anything() => AppendAtom(".*");

    /// <summary>.+ — one or more of anything.</summary>
    public FluentRegex Something() => AppendAtom(".+");

    // =====================================================================
    // Quantifiers — apply to the LAST atom appended
    // =====================================================================

    /// <summary>{n} — the previous atom exactly n times.</summary>
    public FluentRegex Exactly(int n) => Quantify("{" + n + "}");

    /// <summary>{n,} — the previous atom at least n times.</summary>
    public FluentRegex AtLeast(int n) => Quantify("{" + n + ",}");

    /// <summary>{n,m} — the previous atom between n and m times.</summary>
    public FluentRegex Between(int n, int m) => Quantify("{" + n + "," + m + "}");

    /// <summary>+ — the previous atom one or more times.</summary>
    public FluentRegex OneOrMore() => Quantify("+");

    /// <summary>* — the previous atom zero or more times.</summary>
    public FluentRegex ZeroOrMore() => Quantify("*");

    /// <summary>? — the previous atom is optional (zero or one).</summary>
    public FluentRegex Optional() => Quantify("?");

    /// <summary>
    /// Makes the previous quantifier lazy (match as little as possible).
    /// Call immediately after a quantifier: .Anything().Lazy() → .*?
    /// </summary>
    public FluentRegex Lazy() => AppendRaw("?");

    // =====================================================================
    // Groups & backreferences
    // =====================================================================

    /// <summary>(...) — capturing group. Refer back with SameAs(number).</summary>
    public FluentRegex Group(Action<FluentRegex> inner) =>
        AppendAtom("(" + Build(inner) + ")");

    /// <summary>(?&lt;name&gt;...) — named capturing group. Refer back with SameAs(name).</summary>
    public FluentRegex NamedGroup(string name, Action<FluentRegex> inner) =>
        AppendAtom("(?<" + RequireValidGroupName(name) + ">" + Build(inner) + ")");

    /// <summary>(?:...) — group without capturing. Use for quantifying/alternating a sequence.</summary>
    public FluentRegex NonCaptureGroup(Action<FluentRegex> inner) =>
        AppendAtom("(?:" + Build(inner) + ")");

    /// <summary>(?&gt;...) — atomic group: no backtracking inside once matched.</summary>
    public FluentRegex AtomicGroup(Action<FluentRegex> inner) =>
        AppendAtom("(?>" + Build(inner) + ")");

    /// <summary>\n — backreference: match the same text group n captured.</summary>
    public FluentRegex SameAs(int groupNumber) => AppendAtom("\\" + groupNumber);

    /// <summary>\k&lt;name&gt; — backreference to a named group.</summary>
    public FluentRegex SameAs(string groupName) =>
        AppendAtom(@"\k<" + RequireValidGroupName(groupName) + ">");

    // =====================================================================
    // Alternation
    // =====================================================================

    /// <summary>
    /// | — OR. Binds loosely like raw regex: everything-before | everything-after.
    /// Wrap in NonCaptureGroup if you need tighter scope.
    /// </summary>
    public FluentRegex Or(Action<FluentRegex> right)
    {
        AppendRaw("|");
        return AppendRaw(Build(right));
    }

    /// <summary>| followed by an escaped literal.</summary>
    public FluentRegex Or(string literal)
    {
        AppendRaw("|");
        return AppendRaw(Regex.Escape(literal));
    }

    /// <summary>(?:a|b|c) — exactly one of these literals. Scoped and escaped.</summary>
    public FluentRegex OneOf(params string[] literals) =>
        AppendAtom("(?:" + string.Join("|", literals.Select(Regex.Escape)) + ")");

    // =====================================================================
    // Lookarounds — zero-width assertions (peek without consuming)
    // =====================================================================

    /// <summary>(?=...) — positive lookahead: the next text MUST match this.</summary>
    public FluentRegex IfFollowedBy(Action<FluentRegex> inner) =>
        AppendAtom("(?=" + Build(inner) + ")");

    /// <summary>(?=literal) — positive lookahead for an exact string.</summary>
    public FluentRegex IfFollowedBy(string literal) =>
        AppendAtom("(?=" + Regex.Escape(literal) + ")");

    /// <summary>(?!...) — negative lookahead: the next text must NOT match this.</summary>
    public FluentRegex IfNotFollowedBy(Action<FluentRegex> inner) =>
        AppendAtom("(?!" + Build(inner) + ")");

    /// <summary>(?!literal) — negative lookahead for an exact string.</summary>
    public FluentRegex IfNotFollowedBy(string literal) =>
        AppendAtom("(?!" + Regex.Escape(literal) + ")");

    /// <summary>(?&lt;=...) — positive lookbehind: the PRECEDING text must match this.</summary>
    public FluentRegex IfPrecededBy(Action<FluentRegex> inner) =>
        AppendAtom("(?<=" + Build(inner) + ")");

    /// <summary>(?&lt;=literal) — positive lookbehind for an exact string.</summary>
    public FluentRegex IfPrecededBy(string literal) =>
        AppendAtom("(?<=" + Regex.Escape(literal) + ")");

    /// <summary>(?&lt;!...) — negative lookbehind: the preceding text must NOT match this.</summary>
    public FluentRegex IfNotPrecededBy(Action<FluentRegex> inner) =>
        AppendAtom("(?<!" + Build(inner) + ")");

    /// <summary>(?&lt;!literal) — negative lookbehind for an exact string.</summary>
    public FluentRegex IfNotPrecededBy(string literal) =>
        AppendAtom("(?<!" + Regex.Escape(literal) + ")");

    // =====================================================================
    // Options
    // =====================================================================

    /// <summary>Ignore case when matching (RegexOptions.IgnoreCase).</summary>
    public FluentRegex CaseInsensitive() => WithOption(RegexOptions.IgnoreCase);

    /// <summary>^ and $ match at every line, not just string start/end.</summary>
    public FluentRegex MultiLine() => WithOption(RegexOptions.Multiline);

    /// <summary>. also matches newlines (RegexOptions.Singleline).</summary>
    public FluentRegex DotMatchesNewline() => WithOption(RegexOptions.Singleline);

    /// <summary>Compile to IL for faster repeated matching.</summary>
    public FluentRegex Compiled() => WithOption(RegexOptions.Compiled);

    private FluentRegex WithOption(RegexOptions option)
    {
        _options |= option;
        return this;
    }

    // =====================================================================
    // Build / use
    // =====================================================================

    /// <summary>The raw regex pattern string built so far.</summary>
    public string Pattern => _pattern.ToString();

    /// <summary>Build the final Regex (with any options set).</summary>
    public Regex ToRegex() => new(Pattern, _options);

    /// <summary>Build with a match timeout — good hygiene for untrusted input.</summary>
    public Regex ToRegex(TimeSpan matchTimeout) => new(Pattern, _options, matchTimeout);

    /// <summary>Convenience: does the input match?</summary>
    public bool IsMatch(string input) => ToRegex().IsMatch(input);

    /// <summary>Returns the pattern string (same as <see cref="Pattern"/>).</summary>
    public override string ToString() => Pattern;

    // =====================================================================
    // Internals
    // =====================================================================

    /// <summary>Append something quantifiable and remember where it starts.</summary>
    private FluentRegex AppendAtom(string atom)
    {
        _lastAtomStart = _pattern.Length;
        _pattern.Append(atom);
        return this;
    }

    /// <summary>Append something that is never quantified directly (anchors, |, lazy ?).</summary>
    private FluentRegex AppendRaw(string text)
    {
        _pattern.Append(text);
        return this;
    }

    /// <summary>
    /// Apply a quantifier to the last atom. Multi-character atoms that are not
    /// already self-contained get wrapped in (?:...) so the quantifier applies
    /// to the whole atom, not just its final character.
    /// </summary>
    private FluentRegex Quantify(string quantifier)
    {
        if (_lastAtomStart < 0)
            throw new InvalidOperationException(
                "Nothing to quantify — add an atom (Digit, Then, Group, ...) first.");

        int length = _pattern.Length - _lastAtomStart;
        string atom = _pattern.ToString(_lastAtomStart, length);

        if (!IsAtomic(atom))
        {
            _pattern.Length = _lastAtomStart;
            _pattern.Append("(?:").Append(atom).Append(')');
        }

        _pattern.Append(quantifier);
        return this;
    }

    /// <summary>Is this atom safe to quantify without wrapping?</summary>
    private static bool IsAtomic(string atom)
    {
        if (atom.Length == 1) return true;                          // a, \n handled below
        if (atom.Length == 2 && atom[0] == '\\') return true;       // \d, \1, \.
        if (atom[0] == '[' && atom[^1] == ']'
            && atom.IndexOf(']', 1) == atom.Length - 1) return true; // one char class
        if (atom[0] == '(' && atom[^1] == ')') return true;          // one group (built per-atom)
        return false;
    }

    /// <summary>Run a sub-builder and return its pattern text.</summary>
    private static string Build(Action<FluentRegex> inner)
    {
        var sub = new FluentRegex();
        inner(sub);
        return sub.Pattern;
    }

    /// <summary>Reject an empty character set — "[]" and "[^]" are invalid regex.</summary>
    private static string RequireChars(string chars)
    {
        if (string.IsNullOrEmpty(chars))
            throw new ArgumentException(
                "Character set cannot be empty — a class like [] never matches anything.",
                nameof(chars));
        return chars;
    }

    /// <summary>
    /// Group names must be word characters and not start with a digit
    /// (a leading digit means "numbered group" to the .NET engine).
    /// </summary>
    private static string RequireValidGroupName(string name)
    {
        if (string.IsNullOrEmpty(name)
            || char.IsDigit(name[0])
            || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
            throw new ArgumentException(
                $"'{name}' is not a valid group name — use letters, digits, or '_', " +
                "not starting with a digit.", nameof(name));
        return name;
    }

    /// <summary>Escape characters that are special INSIDE a character class.</summary>
    private static string EscapeForClass(string chars)
    {
        var sb = new StringBuilder(chars.Length);
        foreach (char c in chars)
            sb.Append(EscapeCharForClass(c));
        return sb.ToString();
    }

    private static string EscapeCharForClass(char c) =>
        c is '\\' or ']' or '^' or '-' ? "\\" + c : c.ToString();
}
