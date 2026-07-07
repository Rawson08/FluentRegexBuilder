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

    /// <summary>
    /// Start building a new pattern.
    /// <para>Example: <c>FluentRegex.Create().Digit().OneOrMore().ToRegex()</c> → regex <c>\d+</c></para>
    /// </summary>
    public static FluentRegex Create() => new();

    // =====================================================================
    // Anchors — positions, not characters
    // =====================================================================

    /// <summary>
    /// ^ — start of line (or string).
    /// <para>Example: <c>.StartOfLine().Then("Hello")</c> → <c>^Hello</c> — matches "Hello world", not "Say Hello"</para>
    /// </summary>
    public FluentRegex StartOfLine() => AppendRaw("^");

    /// <summary>
    /// $ — end of line (or string).
    /// <para>Example: <c>.Then(".pdf").EndOfLine()</c> → <c>\.pdf$</c> — matches "report.pdf", not "file.pdf.bak"</para>
    /// </summary>
    public FluentRegex EndOfLine() => AppendRaw("$");

    /// <summary>
    /// \A — start of the entire string, even in multiline mode.
    /// <para>Example: <c>.StartOfString().Then("#!")</c> → <c>\A\#!</c> — matches only if the string begins with "#!"</para>
    /// </summary>
    public FluentRegex StartOfString() => AppendRaw(@"\A");

    /// <summary>
    /// \z — absolute end of the entire string.
    /// <para>Example: <c>.Then("END").EndOfString()</c> → <c>END\z</c> — no trailing newline allowed after "END"</para>
    /// </summary>
    public FluentRegex EndOfString() => AppendRaw(@"\z");

    /// <summary>
    /// \b — word boundary (edge between \w and non-\w).
    /// <para>Example: <c>.WordBoundary().Then("cat").WordBoundary()</c> → <c>\bcat\b</c> — matches "the cat", not "scatter"</para>
    /// </summary>
    public FluentRegex WordBoundary() => AppendRaw(@"\b");

    /// <summary>
    /// \B — NOT a word boundary.
    /// <para>Example: <c>.NonWordBoundary().Then("cat")</c> → <c>\Bcat</c> — matches the "cat" in "scatter", not in "cat food"</para>
    /// </summary>
    public FluentRegex NonWordBoundary() => AppendRaw(@"\B");

    // =====================================================================
    // Literals
    // =====================================================================

    /// <summary>
    /// Match this exact text. Special characters are escaped for you.
    /// <para>Example: <c>.Then("v1.2 ($)")</c> → <c>v1\.2\ \(\$\)</c> — the dot, parens, and $ mean themselves</para>
    /// </summary>
    public FluentRegex Then(string literal) => AppendAtom(Regex.Escape(literal));

    /// <summary>
    /// Alias for Then — reads better at the start of a chain.
    /// <para>Example: <c>FluentRegex.Create().Find("error:")</c> → <c>error:</c></para>
    /// </summary>
    public FluentRegex Find(string literal) => Then(literal);

    /// <summary>
    /// Optionally match this exact text: (?:literal)?
    /// <para>Example: <c>.Then("http").Maybe("s")</c> → <c>http(?:s)?</c> — matches both "http" and "https"</para>
    /// </summary>
    public FluentRegex Maybe(string literal) =>
        AppendAtom("(?:" + Regex.Escape(literal) + ")?");

    // =====================================================================
    // Character classes & shorthands
    // =====================================================================

    /// <summary>
    /// \d — any digit 0–9.
    /// <para>Example: <c>.Digit().Exactly(3)</c> → <c>\d{3}</c> — matches "042", not "42" or "4x2"</para>
    /// </summary>
    public FluentRegex Digit() => AppendAtom(@"\d");

    /// <summary>
    /// \D — any character that is NOT a digit.
    /// <para>Example: <c>.NonDigit().OneOrMore()</c> → <c>\D+</c> — matches "abc-", stops at the first digit</para>
    /// </summary>
    public FluentRegex NonDigit() => AppendAtom(@"\D");

    /// <summary>
    /// \w — letter, digit, or underscore.
    /// <para>Example: <c>.WordChar().OneOrMore()</c> → <c>\w+</c> — matches "user_42", stops at "@" or space</para>
    /// </summary>
    public FluentRegex WordChar() => AppendAtom(@"\w");

    /// <summary>
    /// \W — anything that is NOT a word character.
    /// <para>Example: <c>.NonWordChar()</c> → <c>\W</c> — matches "@", " ", "-", but not "a", "7", "_"</para>
    /// </summary>
    public FluentRegex NonWordChar() => AppendAtom(@"\W");

    /// <summary>
    /// \s — any whitespace (space, tab, newline...).
    /// <para>Example: <c>.Then("hello").Whitespace().OneOrMore().Then("world")</c> → <c>hello\s+world</c></para>
    /// </summary>
    public FluentRegex Whitespace() => AppendAtom(@"\s");

    /// <summary>
    /// \S — anything that is NOT whitespace.
    /// <para>Example: <c>.NonWhitespace().OneOrMore()</c> → <c>\S+</c> — grabs one "word" of any non-space characters</para>
    /// </summary>
    public FluentRegex NonWhitespace() => AppendAtom(@"\S");

    /// <summary>
    /// . — any single character (except newline unless DotMatchesNewline()).
    /// <para>Example: <c>.Then("gr").AnyChar().Then("y")</c> → <c>gr.y</c> — matches "gray" and "grey"</para>
    /// </summary>
    public FluentRegex AnyChar() => AppendAtom(".");

    /// <summary>
    /// \t — a tab character.
    /// <para>Example: <c>.WordChar().OneOrMore().Tab().Digit().OneOrMore()</c> → <c>\w+\t\d+</c> — a TSV pair</para>
    /// </summary>
    public FluentRegex Tab() => AppendAtom(@"\t");

    /// <summary>
    /// Matches \r\n, \r, or \n.
    /// <para>Example: <c>.Then("---").LineBreak()</c> → <c>---(?:\r\n|\r|\n)</c> — works on Windows and Unix files</para>
    /// </summary>
    public FluentRegex LineBreak() => AppendAtom(@"(?:\r\n|\r|\n)");

    /// <summary>
    /// [chars] — any ONE of these characters. Escaped for you.
    /// <para>Example: <c>.AnyOf("aeiou")</c> → <c>[aeiou]</c> — matches a single vowel</para>
    /// </summary>
    public FluentRegex AnyOf(string chars) =>
        AppendAtom("[" + EscapeForClass(RequireChars(chars)) + "]");

    /// <summary>
    /// [^chars] — any ONE character NOT in this set. Escaped for you.
    /// <para>Example: <c>.NoneOf(",;")</c> → <c>[^,;]</c> — one character that is neither comma nor semicolon</para>
    /// </summary>
    public FluentRegex NoneOf(string chars) =>
        AppendAtom("[^" + EscapeForClass(RequireChars(chars)) + "]");

    /// <summary>
    /// [from-to] — one character in this range. Escaped for you.
    /// <para>Example: <c>.Range('A', 'Z').Range('a', 'z').ZeroOrMore()</c> → <c>[A-Z][a-z]*</c> — a capitalized word</para>
    /// </summary>
    public FluentRegex Range(char from, char to) =>
        AppendAtom("[" + EscapeCharForClass(from) + "-" + EscapeCharForClass(to) + "]");

    /// <summary>
    /// Raw character-class content — NOT escaped. The power-user escape hatch.
    /// <para>Example: <c>.Set("A-Za-z0-9")</c> → <c>[A-Za-z0-9]</c>; <c>.Set("0-9", negate: true)</c> → <c>[^0-9]</c></para>
    /// </summary>
    public FluentRegex Set(string classContent, bool negate = false) =>
        AppendAtom("[" + (negate ? "^" : "") + classContent + "]");

    /// <summary>
    /// [^chars]* — zero or more characters, none of which are in the set.
    /// Already quantified — don't add OneOrMore after it.
    /// <para>Example: <c>.Then("&lt;").AnythingBut("&gt;").Then("&gt;")</c> → <c>&lt;[^&gt;]*&gt;</c> — the inside of one HTML tag</para>
    /// </summary>
    public FluentRegex AnythingBut(string chars) =>
        AppendAtom("[^" + EscapeForClass(RequireChars(chars)) + "]*");

    /// <summary>
    /// .* — zero or more of anything.
    /// <para>Example: <c>.Then("[").Anything().Lazy().Then("]")</c> → <c>\[.*?]</c> — the shortest bracketed span</para>
    /// </summary>
    public FluentRegex Anything() => AppendAtom(".*");

    /// <summary>
    /// .+ — one or more of anything.
    /// <para>Example: <c>.Then("=").Something()</c> → <c>=.+</c> — an equals sign followed by at least one character</para>
    /// </summary>
    public FluentRegex Something() => AppendAtom(".+");

    // =====================================================================
    // Quantifiers — apply to the LAST atom appended
    // =====================================================================

    /// <summary>
    /// {n} — the previous atom exactly n times.
    /// <para>Example: <c>.Digit().Exactly(4)</c> → <c>\d{4}</c>; <c>.Then("ab").Exactly(2)</c> → <c>(?:ab){2}</c> (wrapped for you)</para>
    /// </summary>
    public FluentRegex Exactly(int n) => Quantify("{" + n + "}");

    /// <summary>
    /// {n,} — the previous atom at least n times.
    /// <para>Example: <c>.WordChar().AtLeast(8)</c> → <c>\w{8,}</c> — e.g. a minimum password length</para>
    /// </summary>
    public FluentRegex AtLeast(int n) => Quantify("{" + n + ",}");

    /// <summary>
    /// {n,m} — the previous atom between n and m times.
    /// <para>Example: <c>.Digit().Between(2, 4)</c> → <c>\d{2,4}</c> — matches "12", "123", "1234", not "1"</para>
    /// </summary>
    public FluentRegex Between(int n, int m) => Quantify("{" + n + "," + m + "}");

    /// <summary>
    /// + — the previous atom one or more times.
    /// <para>Example: <c>.Digit().OneOrMore()</c> → <c>\d+</c> — matches "7" and "2026", not ""</para>
    /// </summary>
    public FluentRegex OneOrMore() => Quantify("+");

    /// <summary>
    /// * — the previous atom zero or more times.
    /// <para>Example: <c>.Whitespace().ZeroOrMore().Then("=")</c> → <c>\s*=</c> — "=" with any indentation before it</para>
    /// </summary>
    public FluentRegex ZeroOrMore() => Quantify("*");

    /// <summary>
    /// ? — the previous atom is optional (zero or one).
    /// <para>Example: <c>.Then("colou").Then("r").Optional()</c>... simpler: <c>.Then("colo").Maybe("u").Then("r")</c> → <c>colo(?:u)?r</c></para>
    /// </summary>
    public FluentRegex Optional() => Quantify("?");

    /// <summary>
    /// Makes the previous quantifier lazy (match as little as possible).
    /// Call immediately after a quantifier.
    /// <para>Example: <c>.Then("&lt;").Anything().Lazy().Then("&gt;")</c> → <c>&lt;.*?&gt;</c> — matches "&lt;b&gt;" alone, not "&lt;b&gt;text&lt;/b&gt;"</para>
    /// </summary>
    public FluentRegex Lazy() => AppendRaw("?");

    // =====================================================================
    // Groups & backreferences
    // =====================================================================

    /// <summary>
    /// (...) — capturing group. Refer back with SameAs(number).
    /// <para>Example: <c>.Group(g =&gt; g.Digit().OneOrMore())</c> → <c>(\d+)</c> — read it via <c>match.Groups[1].Value</c></para>
    /// </summary>
    public FluentRegex Group(Action<FluentRegex> inner) =>
        AppendAtom("(" + Build(inner) + ")");

    /// <summary>
    /// (?&lt;name&gt;...) — named capturing group. Refer back with SameAs(name).
    /// <para>Example: <c>.NamedGroup("year", g =&gt; g.Digit().Exactly(4))</c> → <c>(?&lt;year&gt;\d{4})</c> — read it via <c>match.Groups["year"].Value</c></para>
    /// </summary>
    public FluentRegex NamedGroup(string name, Action<FluentRegex> inner) =>
        AppendAtom("(?<" + RequireValidGroupName(name) + ">" + Build(inner) + ")");

    /// <summary>
    /// (?:...) — group without capturing. Use for quantifying/alternating a sequence.
    /// <para>Example: <c>.NonCaptureGroup(g =&gt; g.Then("na")).Exactly(2)</c> → <c>(?:na){2}</c> — matches "nana"</para>
    /// </summary>
    public FluentRegex NonCaptureGroup(Action<FluentRegex> inner) =>
        AppendAtom("(?:" + Build(inner) + ")");

    /// <summary>
    /// (?&gt;...) — atomic group: no backtracking inside once matched.
    /// Guards against catastrophic backtracking on hostile input.
    /// <para>Example: <c>.AtomicGroup(g =&gt; g.Digit().OneOrMore())</c> → <c>(?&gt;\d+)</c></para>
    /// </summary>
    public FluentRegex AtomicGroup(Action<FluentRegex> inner) =>
        AppendAtom("(?>" + Build(inner) + ")");

    /// <summary>
    /// \n — backreference: match the same text group n captured.
    /// <para>Example: <c>.Group(g =&gt; g.WordChar().OneOrMore()).Whitespace().SameAs(1)</c> → <c>(\w+)\s\1</c> — finds doubled words like "the the"</para>
    /// </summary>
    public FluentRegex SameAs(int groupNumber) => AppendAtom("\\" + groupNumber);

    /// <summary>
    /// \k&lt;name&gt; — backreference to a named group.
    /// <para>Example: <c>.NamedGroup("q", g =&gt; g.AnyOf("'\""))...SameAs("q")</c> → <c>(?&lt;q&gt;['"])...\k&lt;q&gt;</c> — closing quote must match opening</para>
    /// </summary>
    public FluentRegex SameAs(string groupName) =>
        AppendAtom(@"\k<" + RequireValidGroupName(groupName) + ">");

    // =====================================================================
    // Alternation
    // =====================================================================

    /// <summary>
    /// | — OR. Binds loosely like raw regex: everything-before | everything-after.
    /// Wrap in NonCaptureGroup if you need tighter scope — or use OneOf for literals.
    /// <para>Example: <c>.Then("cat").Or(o =&gt; o.Then("dog"))</c> → <c>cat|dog</c></para>
    /// </summary>
    public FluentRegex Or(Action<FluentRegex> right)
    {
        AppendRaw("|");
        return AppendRaw(Build(right));
    }

    /// <summary>
    /// | followed by an escaped literal.
    /// <para>Example: <c>.Then("yes").Or("no")</c> → <c>yes|no</c></para>
    /// </summary>
    public FluentRegex Or(string literal)
    {
        AppendRaw("|");
        return AppendRaw(Regex.Escape(literal));
    }

    /// <summary>
    /// (?:a|b|c) — exactly one of these literals. Scoped and escaped.
    /// <para>Example: <c>.OneOf("GET", "POST", "PUT")</c> → <c>(?:GET|POST|PUT)</c> — anchors around it apply to the whole set</para>
    /// </summary>
    public FluentRegex OneOf(params string[] literals) =>
        AppendAtom("(?:" + string.Join("|", literals.Select(Regex.Escape)) + ")");

    // =====================================================================
    // Lookarounds — zero-width assertions (peek without consuming)
    // =====================================================================

    /// <summary>
    /// (?=...) — positive lookahead: the next text MUST match this (but isn't consumed).
    /// <para>Example: <c>.Digit().OneOrMore().IfFollowedBy(b =&gt; b.Then("px"))</c> → <c>\d+(?=px)</c> — matches "24" in "24px", skips "24pt"</para>
    /// </summary>
    public FluentRegex IfFollowedBy(Action<FluentRegex> inner) =>
        AppendAtom("(?=" + Build(inner) + ")");

    /// <summary>
    /// (?=literal) — positive lookahead for an exact string.
    /// <para>Example: <c>.Digit().OneOrMore().IfFollowedBy("%")</c> → <c>\d+(?=%)</c> — matches "50" in "50%", the "%" stays unmatched</para>
    /// </summary>
    public FluentRegex IfFollowedBy(string literal) =>
        AppendAtom("(?=" + Regex.Escape(literal) + ")");

    /// <summary>
    /// (?!...) — negative lookahead: the next text must NOT match this.
    /// <para>Example: <c>.IfNotFollowedBy(b =&gt; b.OneOf("000", "666")).Digit().Exactly(3)</c> → <c>(?!(?:000|666))\d{3}</c> — 3 digits, but not those</para>
    /// </summary>
    public FluentRegex IfNotFollowedBy(Action<FluentRegex> inner) =>
        AppendAtom("(?!" + Build(inner) + ")");

    /// <summary>
    /// (?!literal) — negative lookahead for an exact string.
    /// <para>Example: <c>.Then("q").IfNotFollowedBy("u")</c> → <c>q(?!u)</c> — matches the "q" in "qatar", not in "queen"</para>
    /// </summary>
    public FluentRegex IfNotFollowedBy(string literal) =>
        AppendAtom("(?!" + Regex.Escape(literal) + ")");

    /// <summary>
    /// (?&lt;=...) — positive lookbehind: the PRECEDING text must match this.
    /// <para>Example: <c>.IfPrecededBy(b =&gt; b.Then("$")).Digit().OneOrMore()</c> → <c>(?&lt;=\$)\d+</c> — matches "20" in "$20", not in "20kg"</para>
    /// </summary>
    public FluentRegex IfPrecededBy(Action<FluentRegex> inner) =>
        AppendAtom("(?<=" + Build(inner) + ")");

    /// <summary>
    /// (?&lt;=literal) — positive lookbehind for an exact string.
    /// <para>Example: <c>.IfPrecededBy("ID:").Digit().OneOrMore()</c> → <c>(?&lt;=ID:)\d+</c> — matches "42" in "ID:42"</para>
    /// </summary>
    public FluentRegex IfPrecededBy(string literal) =>
        AppendAtom("(?<=" + Regex.Escape(literal) + ")");

    /// <summary>
    /// (?&lt;!...) — negative lookbehind: the preceding text must NOT match this.
    /// <para>Example: <c>.IfNotPrecededBy(b =&gt; b.Then("-")).Digit().OneOrMore()</c> → <c>(?&lt;!-)\d+</c> — skips digits right after a minus sign</para>
    /// </summary>
    public FluentRegex IfNotPrecededBy(Action<FluentRegex> inner) =>
        AppendAtom("(?<!" + Build(inner) + ")");

    /// <summary>
    /// (?&lt;!literal) — negative lookbehind for an exact string.
    /// <para>Example: <c>.IfNotPrecededBy("no-").Then("reply")</c> → <c>(?&lt;!no-)reply</c> — matches "reply", not the tail of "no-reply"</para>
    /// </summary>
    public FluentRegex IfNotPrecededBy(string literal) =>
        AppendAtom("(?<!" + Regex.Escape(literal) + ")");

    // =====================================================================
    // Options
    // =====================================================================

    /// <summary>
    /// Ignore case when matching (RegexOptions.IgnoreCase).
    /// <para>Example: <c>.Then("hello").CaseInsensitive().ToRegex()</c> — matches "hello", "HELLO", "HeLLo"</para>
    /// </summary>
    public FluentRegex CaseInsensitive() => WithOption(RegexOptions.IgnoreCase);

    /// <summary>
    /// ^ and $ match at every line, not just string start/end (RegexOptions.Multiline).
    /// <para>Example: <c>.StartOfLine().Then("TODO").MultiLine()</c> — finds "TODO" at the start of ANY line in a file</para>
    /// </summary>
    public FluentRegex MultiLine() => WithOption(RegexOptions.Multiline);

    /// <summary>
    /// . also matches newlines (RegexOptions.Singleline).
    /// <para>Example: <c>.Then("/*").Anything().Lazy().Then("*/").DotMatchesNewline()</c> — matches a block comment spanning lines</para>
    /// </summary>
    public FluentRegex DotMatchesNewline() => WithOption(RegexOptions.Singleline);

    /// <summary>
    /// Compile to IL for faster repeated matching (RegexOptions.Compiled).
    /// Worth it for a pattern stored in a static field and used many times.
    /// </summary>
    public FluentRegex Compiled() => WithOption(RegexOptions.Compiled);

    private FluentRegex WithOption(RegexOptions option)
    {
        _options |= option;
        return this;
    }

    // =====================================================================
    // Build / use
    // =====================================================================

    /// <summary>
    /// The raw regex pattern string built so far.
    /// <para>Handy for pasting into [GeneratedRegex("...")], which needs a compile-time constant.</para>
    /// </summary>
    public string Pattern => _pattern.ToString();

    /// <summary>
    /// Build the final Regex (with any options set). The usual last call in a chain.
    /// <para>Example: <c>var regex = FluentRegex.Create().Digit().OneOrMore().ToRegex();</c></para>
    /// </summary>
    public Regex ToRegex() => new(Pattern, _options);

    /// <summary>
    /// Build with a match timeout — good hygiene for untrusted input.
    /// <para>Example: <c>.ToRegex(TimeSpan.FromMilliseconds(250))</c> — a hostile string throws instead of hanging the CPU</para>
    /// </summary>
    public Regex ToRegex(TimeSpan matchTimeout) => new(Pattern, _options, matchTimeout);

    /// <summary>
    /// Convenience: does the input match?
    /// <para>Example: <c>FluentRegex.Create().Digit().Exactly(5).IsMatch("90210")</c> → <c>true</c></para>
    /// </summary>
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
