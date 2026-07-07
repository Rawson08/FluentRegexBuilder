using System.Text.RegularExpressions;
using FluentRegexBuilder;
using Xunit;

namespace FluentRegexBuilder.Tests;

// =========================================================================
// Pattern generation — asserts the exact regex text each chain produces.
// These double as documentation: every test shows chain → pattern.
// =========================================================================
public class PatternGenerationTests
{
    [Fact]
    public void Url_GeneratesExpectedPattern()
    {
        var pattern = FluentRegex.Create()
            .StartOfLine()
            .Then("http").Maybe("s").Then("://").Maybe("www.")
            .AnythingBut(" ")
            .EndOfLine()
            .Pattern;

        Assert.Equal(@"^http(?:s)?://(?:www\.)?[^ ]*$", pattern);
    }

    [Fact]
    public void Then_EscapesSpecialCharacters()
    {
        var pattern = FluentRegex.Create().Then("price: $1.50 (usd)").Pattern;

        // '.', '$', '(', ')' must all be escaped; a match proves it
        Assert.Matches(new Regex(pattern), "price: $1.50 (usd)");
        Assert.DoesNotMatch(new Regex(pattern), "price: $1X50 (usd)");
    }

    [Fact]
    public void Quantifier_WrapsMultiCharLiteral()
    {
        var pattern = FluentRegex.Create().Then("abc").Exactly(2).Pattern;
        Assert.Equal("(?:abc){2}", pattern);
    }

    [Fact]
    public void Quantifier_DoesNotWrapShorthandClass()
    {
        var pattern = FluentRegex.Create().Digit().Exactly(3).Pattern;
        Assert.Equal(@"\d{3}", pattern);
    }

    [Fact]
    public void Quantifier_DoesNotWrapCharacterClass()
    {
        var pattern = FluentRegex.Create().AnyOf("abc").OneOrMore().Pattern;
        Assert.Equal("[abc]+", pattern);
    }

    [Fact]
    public void Lazy_AppendsQuestionMarkToQuantifier()
    {
        var pattern = FluentRegex.Create().Anything().Lazy().Pattern;
        Assert.Equal(".*?", pattern);
    }

    [Fact]
    public void Quantify_WithNoAtom_Throws()
    {
        Assert.Throws<InvalidOperationException>(
            () => FluentRegex.Create().Exactly(2));
    }

    [Fact]
    public void Range_EscapesClassSpecialCharacters()
    {
        var pattern = FluentRegex.Create().Range(']', '^').Pattern;

        Assert.Equal(@"[\]-\^]", pattern);
        Assert.Matches(new Regex(pattern), "]");
        Assert.Matches(new Regex(pattern), "^");
        Assert.DoesNotMatch(new Regex(pattern), "a");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyCharacterSets_Throw(string? chars)
    {
        Assert.Throws<ArgumentException>(() => FluentRegex.Create().AnyOf(chars!));
        Assert.Throws<ArgumentException>(() => FluentRegex.Create().NoneOf(chars!));
        Assert.Throws<ArgumentException>(() => FluentRegex.Create().AnythingBut(chars!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1stDigit")]   // leading digit = numbered group to the engine
    [InlineData("bad>name")]   // '>' would terminate the group early
    [InlineData("has space")]
    public void InvalidGroupNames_Throw(string name)
    {
        Assert.Throws<ArgumentException>(
            () => FluentRegex.Create().NamedGroup(name, g => g.Digit()));
        Assert.Throws<ArgumentException>(
            () => FluentRegex.Create().SameAs(name));
    }

    [Fact]
    public void NamedGroup_GeneratesDotNetSyntax()
    {
        var pattern = FluentRegex.Create()
            .NamedGroup("area", g => g.Digit().Exactly(3))
            .Pattern;

        Assert.Equal(@"(?<area>\d{3})", pattern);
    }

    [Fact]
    public void NoneOf_EscapesClassSpecialCharacters()
    {
        var pattern = FluentRegex.Create().NoneOf(@"]^-\").Pattern;
        Assert.Equal(@"[^\]\^\-\\]", pattern);
    }

    [Fact]
    public void Lookbehinds_GenerateCorrectSyntax()
    {
        var pattern = FluentRegex.Create()
            .IfPrecededBy("$")
            .Digit().OneOrMore()
            .IfNotPrecededBy(b => b.Then("#"))
            .Pattern;

        Assert.Equal(@"(?<=\$)\d+(?<!\#)", pattern);
    }
}

// =========================================================================
// SSN validator — the pattern this library was born from.
// =========================================================================
public class SsnValidatorTests
{
    private static readonly Regex Ssn = FluentRegex.Create()
        .StartOfLine()
        .IfNotFollowedBy(b => b               // reject all-same-digit
            .Group(g => g.Digit())            // group 1
            .SameAs(1).Exactly(2)
            .Then("-").SameAs(1).Exactly(2)
            .Then("-").SameAs(1).Exactly(4))
        .IfNotFollowedBy(b => b               // reject bad areas
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

    [Fact]
    public void GeneratesExpectedPattern() =>
        Assert.Equal(
            @"^(?!(\d)\1{2}-\1{2}-\1{4})(?!(?:000|666)|9\d{2})\d{3}-(?!00)\d{2}-(?!0000)\d{4}$",
            Ssn.ToString());

    [Theory]
    [InlineData("123-45-6780")]
    [InlineData("223-45-6789")]
    [InlineData("899-99-9999")]
    public void ValidSsns_Match(string input) => Assert.Matches(Ssn, input);

    [Theory]
    [InlineData("000-12-3456")] // 000 area
    [InlineData("666-12-3456")] // 666 area
    [InlineData("900-12-3456")] // 9xx area
    [InlineData("999-12-3456")] // 9xx area
    [InlineData("123-00-4567")] // 00 group
    [InlineData("123-45-0000")] // 0000 serial
    [InlineData("111-11-1111")] // repeating digit
    [InlineData("333-33-3333")] // repeating digit
    [InlineData("123456789")]   // missing dashes
    [InlineData("123-45-678")]  // too short
    [InlineData("123-45-67890")]// too long
    [InlineData("")]
    public void InvalidSsns_DoNotMatch(string input) => Assert.DoesNotMatch(Ssn, input);
}

// =========================================================================
// SSN with optional-but-consistent dashes (capture the separator, \2 it).
// =========================================================================
public class SsnOptionalDashTests
{
    private static readonly Regex Ssn = FluentRegex.Create()
        .StartOfLine()
        .IfNotFollowedBy(b => b
            .Group(g => g.Digit())            // group 1: the repeated digit
            .SameAs(1).Exactly(2)
            .Maybe("-").SameAs(1).Exactly(2)
            .Maybe("-").SameAs(1).Exactly(4))
        .IfNotFollowedBy(b => b
            .OneOf("000", "666")
            .Or(o => o.Then("9").Digit().Exactly(2)))
        .Digit().Exactly(3)
        .Group(s => s.Maybe("-"))             // group 2: "" or "-"
        .IfNotFollowedBy("00")
        .Digit().Exactly(2)
        .SameAs(2)                            // second separator must match first
        .IfNotFollowedBy("0000")
        .Digit().Exactly(4)
        .EndOfLine()
        .ToRegex();

    [Theory]
    [InlineData("123-45-6789")]
    [InlineData("123456789")]
    public void BothFormats_Match(string input) => Assert.Matches(Ssn, input);

    [Theory]
    [InlineData("123-456789")]  // mixed separators
    [InlineData("12345-6789")]  // mixed separators
    [InlineData("111111111")]   // repeating, no dashes
    [InlineData("111-11-1111")] // repeating, dashes
    public void InvalidForms_DoNotMatch(string input) => Assert.DoesNotMatch(Ssn, input);
}

// =========================================================================
// Smaller patterns from the challenge ladder.
// =========================================================================
public class ChallengePatternTests
{
    [Theory]
    [InlineData("87124", true)]
    [InlineData("87124-2201", true)]
    [InlineData("87124-", false)]     // dangling dash
    [InlineData("871242201", false)]  // +4 without dash
    [InlineData("8712", false)]
    public void ZipCode_OptionalPlus4_IsOneUnit(string input, bool expected)
    {
        var zip = FluentRegex.Create()
            .StartOfLine()
            .Digit().Exactly(5)
            .NonCaptureGroup(p => p.Then("-").Digit().Exactly(4))
            .Optional()
            .EndOfLine()
            .ToRegex();

        Assert.Equal(expected, zip.IsMatch(input));
    }

    [Theory]
    [InlineData("Rent_CalculatedAmount", true)]
    [InlineData("Lease_Id", true)]
    [InlineData("rent_amount", false)]  // lowercase start
    [InlineData("Rent__Amount", false)] // double underscore
    [InlineData("_Rent", false)]        // leading underscore
    [InlineData("Rent", false)]         // no underscore at all
    public void SqlNamingConvention_PascalCaseBothSides(string input, bool expected)
    {
        var name = FluentRegex.Create()
            .StartOfLine()
            .Range('A', 'Z').Set("A-Za-z0-9").ZeroOrMore()
            .Then("_")
            .Range('A', 'Z').Set("A-Za-z0-9").ZeroOrMore()
            .EndOfLine()
            .ToRegex();

        Assert.Equal(expected, name.IsMatch(input));
    }

    [Theory]
    [InlineData("check the the config", true)]
    [InlineData("The the settings", true)]     // case-insensitive
    [InlineData("check the theme config", false)]
    [InlineData("all words differ here", false)]
    public void DuplicateWord_Detector(string input, bool expected)
    {
        var dupe = FluentRegex.Create()
            .WordBoundary()
            .Group(g => g.WordChar().OneOrMore())
            .Whitespace().OneOrMore()
            .SameAs(1)
            .WordBoundary()
            .CaseInsensitive()
            .ToRegex();

        Assert.Equal(expected, dupe.IsMatch(input));
    }

    [Fact]
    public void PayStatementFilename_NamedGroupsExtract()
    {
        var file = FluentRegex.Create()
            .StartOfLine()
            .Then("PayStatement_")
            .NamedGroup("employeeId", id => id.Then("E").Digit().Exactly(5))
            .Then("_")
            .NamedGroup("date", d => d
                .Digit().Exactly(4).Then("-")
                .Digit().Exactly(2).Then("-")
                .Digit().Exactly(2))
            .Then(".pdf")
            .EndOfLine()
            .CaseInsensitive()
            .ToRegex();

        var match = file.Match("PayStatement_E04512_2026-06-15.PDF");

        Assert.True(match.Success);
        Assert.Equal("E04512", match.Groups["employeeId"].Value);
        Assert.Equal("2026-06-15", match.Groups["date"].Value);
    }

    [Theory]
    [InlineData("123456789", true)]   // 1 != 9
    [InlineData("123456781", false)]  // first == last
    [InlineData("12345678", false)]   // too short
    public void FirstAndLastDigit_MustDiffer(string input, bool expected)
    {
        var pattern = FluentRegex.Create()
            .StartOfLine()
            .Group(g => g.Digit())
            .Digit().Exactly(7)
            .IfNotFollowedBy(b => b.SameAs(1))
            .Digit()
            .EndOfLine()
            .ToRegex();

        Assert.Equal(expected, pattern.IsMatch(input));
    }
}

// =========================================================================
// Options & misc behavior.
// =========================================================================
public class OptionsTests
{
    [Fact]
    public void CaseInsensitive_SetsOption()
    {
        var regex = FluentRegex.Create().Then("hello").CaseInsensitive().ToRegex();
        Assert.True(regex.Options.HasFlag(RegexOptions.IgnoreCase));
        Assert.Matches(regex, "HeLLo world");
    }

    [Fact]
    public void ToRegex_WithTimeout_AppliesIt()
    {
        var regex = FluentRegex.Create().Digit().OneOrMore()
            .ToRegex(TimeSpan.FromMilliseconds(250));

        Assert.Equal(TimeSpan.FromMilliseconds(250), regex.MatchTimeout);
    }

    [Fact]
    public void Or_BindsLoosely_LikeRawRegex()
    {
        // cat|dog with no grouping: whole-left vs whole-right
        var regex = FluentRegex.Create().Then("cat").Or("dog").ToRegex();
        Assert.Matches(regex, "hotdog");   // matches "dog" anywhere — documented behavior
        Assert.Matches(regex, "catfish");
    }

    [Fact]
    public void OneOf_IsScoped_UnlikeRawOr()
    {
        var regex = FluentRegex.Create()
            .StartOfLine().OneOf("cat", "dog").EndOfLine().ToRegex();

        Assert.Matches(regex, "cat");
        Assert.DoesNotMatch(regex, "hotdog"); // anchors apply to the whole group
    }
}
