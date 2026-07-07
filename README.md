# FluentRegexBuilder

> *"Some people, when confronted with a problem, think 'I know, I'll use regular expressions.' Now they have two problems."* — Jamie Zawinski, 1997. Still true.

You know regex. Of course you know regex. You've "known" regex for years — which is to say, every single time you need one, you open a new tab and search *"regex lookbehind syntax"* like it's the first day of your career.

Is lookbehind `(?<=` or `(?=<`? Is a non-capturing group `(?:` or `(:?`? Does `{2,}` mean "two or more" or "I have made a terrible mistake"? Why does `.` match everything except the one thing you wanted? Nobody knows. Nobody has *ever* known. Regex is a language designed by people who charged by the character.

**FluentRegexBuilder** lets you write the pattern the way you'd *say* it, and generates the line noise for you:

```csharp
var url = FluentRegex.Create()
    .StartOfLine()
    .Then("http")
    .Maybe("s")
    .Then("://")
    .Maybe("www.")
    .AnythingBut(" ")
    .EndOfLine()
    .ToRegex();

// Generates: ^http(?:s)?://(?:www\.)?[^ ]*$
// You wrote zero backslashes. You escaped nothing. You feel nothing but peace.
```

It's still 100% real .NET `Regex` underneath — this is a *builder*, not a new engine. You get a normal `Regex` object at the end; we just spare you the part where you count backslashes at 11 PM.

## Install

```
dotnet add package FluentRegexBuilder
```

Targets .NET 8 and .NET 10. Zero dependencies. Zero opinions about your tab-vs-spaces stance.

## The "I definitely can't remember that" translation table

| You wanted to say | You were supposed to remember | Now you write |
|---|---|---|
| start / end of line | `^` `$` | `StartOfLine()` `EndOfLine()` |
| start / end of the *whole string*, honest | `\A` `\z` | `StartOfString()` `EndOfString()` |
| edge of a word | `\b` (no, not backspace) | `WordBoundary()` |
| this exact text, dots and all | `abc\.\$\(` *(escape it yourself, coward)* | `Then("abc.$(")` — escaped for you |
| maybe this text | `(?:abc)?` | `Maybe("abc")` |
| a digit / letter / space | `\d` `\w` `\s` | `Digit()` `WordChar()` `Whitespace()` |
| one of these characters | `[abc]` | `AnyOf("abc")` |
| anything *except* these | `[^abc]` | `NoneOf("abc")` |
| a range | `[a-z]` | `Range('a', 'z')` |
| exactly 3 / at least 2 / 2 to 5 | `{3}` `{2,}` `{2,5}` | `Exactly(3)` `AtLeast(2)` `Between(2, 5)` |
| one or more / any amount / optional | `+` `*` `?` | `OneOrMore()` `ZeroOrMore()` `Optional()` |
| don't be greedy about it | `*?` *(a question mark that means something different here)* | `.ZeroOrMore().Lazy()` |
| remember this bit | `(...)` | `Group(g => ...)` |
| remember it *by name* | `(?<name>...)` | `NamedGroup("name", g => ...)` |
| group without remembering | `(?:...)` | `NonCaptureGroup(g => ...)` |
| the same thing group 1 matched | `\1` | `SameAs(1)` |
| same, but by name | `\k<name>` | `SameAs("name")` |
| only if followed by | `(?=...)` | `IfFollowedBy(...)` |
| only if NOT followed by | `(?!...)` | `IfNotFollowedBy(...)` |
| only if preceded by | `(?<=...)` — *not* `(?=<`, you've been burned before | `IfPrecededBy(...)` |
| only if NOT preceded by | `(?<!...)` | `IfNotPrecededBy(...)` |
| this or that, scoped sanely | `(?:cat\|dog)` | `OneOf("cat", "dog")` |
| ignore case | `RegexOptions.IgnoreCase` | `CaseInsensitive()` |

Every method carries XML docs showing the exact regex it emits — so IntelliSense doubles as the cheat sheet you were going to google anyway.

## A real one: SSN validation

Here is a genuinely correct SSN pattern, in its natural habitat:

```
^(?!(\d)\1{2}-\1{2}-\1{4})(?!000|666|9\d{2})\d{3}-(?!00)\d{2}-(?!0000)\d{4}$
```

Beautiful. Correct. Utterly unreviewable. Six months from now this line will have a `// do not touch` comment above it and a small shrine beside it.

The same rules, in a form your code reviewer can actually review:

```csharp
var ssn = FluentRegex.Create()
    .StartOfLine()
    .IfNotFollowedBy(b => b               // reject 111-11-1111 and friends
        .Group(g => g.Digit())            // capture the first digit → group 1
        .SameAs(1).Exactly(2)
        .Then("-").SameAs(1).Exactly(2)
        .Then("-").SameAs(1).Exactly(4))
    .IfNotFollowedBy(b => b               // reject invalid area numbers
        .OneOf("000", "666")
        .Or(o => o.Then("9").Digit().Exactly(2)))
    .Digit().Exactly(3)
    .Then("-")
    .IfNotFollowedBy("00")
    .Digit().Exactly(2)
    .Then("-")
    .IfNotFollowedBy("0000")
    .Digit().Exactly(4)
    .EndOfLine()
    .ToRegex();
```

Yes, that's negative lookaheads with backreferences into a capture group, and it reads like a checklist instead of a cry for help.

## Things it quietly saves you from

- **The escaping game.** Literals passed to `Then`, `Maybe`, `OneOf`, and the lookaround overloads are `Regex.Escape`d automatically. `.` means dot. `$` means dollar sign. `(` will not silently open a group and break capture numbering three lines away.
- **The classic quantifier bug.** In raw regex, `abc{2}` matches `abcc` — the `{2}` only grabs the `c`, and this exact bug has shipped to production more times than anyone will admit. Here, `.Then("abc").Exactly(2)` produces `(?:abc){2}`. The whole thing. Twice. Like you meant.
- **Catastrophic backtracking roulette.** `ToRegex(TimeSpan)` sets a match timeout, so untrusted input can't put your CPU into interpretive dance. There's `AtomicGroup(...)` too, if you know what that is (and if you do — hello, fellow person of suffering).
- **The "nothing to quantify" bug.** `FluentRegex.Create().Exactly(3)` throws a clear exception instead of silently producing garbage.

And when you *do* know exactly what you want, `Set("A-Za-z0-9")` is the raw, unescaped escape hatch. We're a builder, not your manager.

## Using with `[GeneratedRegex]`

The source generator wants a compile-time constant string, so use the builder as a design-time tool: build the chain, print `.Pattern`, paste the result into the attribute, and keep the fluent version in a test as living documentation. Best of both worlds — readable source of truth, zero runtime cost.

## When NOT to use this

Honesty corner: regex is a lingua franca. `\d+` is shorter than `.Digit().OneOrMore()` and you'll meet it again in JavaScript, SQL, grep, and the darker corners of your CI config — for trivial patterns, just learn the three characters. This library earns its keep on the patterns complex enough to need comments: multi-lookahead validation, backreference tricks, anything where a reviewer would otherwise just approve the PR on vibes.

## License

MIT. Take it, use it, ship it. The regexes it generates are yours; the trauma that inspired it remains ours.
