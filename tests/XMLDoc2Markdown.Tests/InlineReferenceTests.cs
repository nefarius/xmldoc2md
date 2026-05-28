using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using Xunit;

namespace XMLDoc2Markdown.Tests;

/// <summary>
/// Tests for inline-reference rendering: <c>&lt;see langword&gt;</c>,
/// <c>&lt;see href&gt;</c>, <c>&lt;typeparamref&gt;</c>, and the
/// "is null / is ." gap regression.
/// </summary>
public class InlineReferenceTests
{
    private static readonly Assembly FixturesAssembly = typeof(InlineRefHost).Assembly;

    private static XmlDocumentation BuildDocFromXml(params (string id, string xml)[] members)
    {
        var elements = members.Select(m => XElement.Parse($"""<member name="{m.id}">{m.xml}</member>"""));
        return new XmlDocumentation("XMLDoc2Markdown.TestFixtures", elements);
    }

    private static TypeDocumentationOptions DefaultOptions() => new()
    {
        GitHubPages = false,
        LinkGenericArguments = true,
    };

    private string GenerateDoc(Type type, XmlDocumentation xmlDoc = null, TypeDocumentationOptions opts = null)
    {
        xmlDoc ??= BuildDocFromXml();
        opts ??= DefaultOptions();
        var ctx = new XmlDocumentationContext(xmlDoc);
        return new TypeDocumentation(FixturesAssembly, type, ctx, opts).ToString();
    }

    // ─── <see langword="..."/> ────────────────────────────────────────────────

    [Fact]
    public void SeeLangword_Null_RendersAsInlineCode()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Pass <see langword=\"null\"/> to opt out.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("`null`");
    }

    [Fact]
    public void SeeLangword_True_RendersAsInlineCode()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Use <see langword=\"true\"/> to enable.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("`true`");
    }

    [Fact]
    public void SeeLangword_False_RendersAsInlineCode()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Returns <see langword=\"false\"/> on failure.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("`false`");
    }

    // ─── Gap regression: "param is ." ─────────────────────────────────────────

    [Fact]
    public void SeeLangword_IsNullGap_DoesNotProduceEmptyPhrase()
    {
        var xmlDoc = BuildDocFromXml(
            ("P:XMLDoc2Markdown.TestFixtures.InlineRefHost.NullableValue",
             "<summary>The value is <see langword=\"null\"/> when not set.</summary>"),
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost", "<summary>Host.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        // Must never produce the "is ." empty-gap pattern
        output.Should().NotContain("is .");
        output.Should().Contain("`null`");
    }

    // ─── <see href="...">text</see> ───────────────────────────────────────────

    [Fact]
    public void SeeHref_WithText_RendersAsMarkdownLink()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>See <see href=\"https://example.com/docs\">external docs</see>.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("[external docs](https://example.com/docs)");
    }

    [Fact]
    public void SeeHref_WithoutText_UsesHrefAsLinkText()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>See <see href=\"https://example.com/api\"/> for more.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("[https://example.com/api](https://example.com/api)");
    }

    [Fact]
    public void SeeHref_UrlNotLost_OutputContainsHref()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Refer to <see href=\"https://learn.microsoft.com/x\">docs</see>.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("https://learn.microsoft.com/x");
        output.Should().Contain("docs");
    }

    // ─── <typeparamref name="T"/> ─────────────────────────────────────────────

    [Fact]
    public void Typeparamref_RendersAsInlineCode()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Works with type <typeparamref name=\"T\"/>.</summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("`T`");
    }

    [Fact]
    public void Typeparamref_OnMethod_RendersAsInlineCode()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost", "<summary>Host.</summary>"),
            ("M:XMLDoc2Markdown.TestFixtures.InlineRefHost.Process``1(``0)",
             "<summary>Processes a value of type <typeparamref name=\"T\"/>.</summary><typeparam name=\"T\">Element type.</typeparam>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("`T`");
    }

    // ─── seealso with href ────────────────────────────────────────────────────

    [Fact]
    public void SeeAlsoHref_RendersAsMarkdownLink()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.InlineRefHost",
             "<summary>Host.<seealso href=\"https://example.com\">See also</seealso></summary>")
        );
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);
        output.Should().Contain("[See also](https://example.com)");
    }

    // ─── Combined fixture member (round-trip via TestFixtures XML) ────────────

    [Fact]
    public void InlineRefHost_GeneratesWithoutEmptyGaps()
    {
        // Use the actual fixtures XML generated by the compiler
        if (!File.Exists(Path.ChangeExtension(FixturesAssembly.Location, ".xml")))
        {
            // If no XML file built, skip — shouldn't happen in CI
            return;
        }

        var xmlDoc = new XmlDocumentation(FixturesAssembly.Location);
        string output = GenerateDoc(typeof(InlineRefHost), xmlDoc);

        // No empty inline-reference gaps
        output.Should().NotMatchRegex(@"is \.\s");
        output.Should().NotMatchRegex(@"with ;\s");
        // Langword renders
        output.Should().Contain("`null`");
        output.Should().Contain("`true`");
        output.Should().Contain("`false`");
        // href link renders
        output.Should().Contain("https://example.com");
        // typeparamref renders
        output.Should().Contain("`T`");
    }
}
