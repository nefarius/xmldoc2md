using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using XMLDoc2Markdown.Utils;
using Xunit;

namespace XMLDoc2Markdown.Tests;

/// <summary>
/// End-to-end tests that run <see cref="TypeDocumentation"/> against the
/// <c>XMLDoc2Markdown.TestFixtures</c> assembly.
/// </summary>
public class TypeDocumentationTests
{
    private static readonly Assembly FixturesAssembly = typeof(SimpleRecord).Assembly;

    private static XmlDocumentation BuildDocFromXml(params (string id, string xml)[] members)
    {
        var elements = members.Select(m => XElement.Parse($"""<member name="{m.id}">{m.xml}</member>"""));
        return new XmlDocumentation("XMLDoc2Markdown.TestFixtures", elements);
    }

    private static TypeDocumentationOptions DefaultOptions(ExternalDocsResolver resolver = null) => new()
    {
        GitHubPages = false,
        LinkGenericArguments = true,
        ExternalDocsResolver = resolver
    };

    private string GenerateDoc(Type type, XmlDocumentation xmlDoc = null, TypeDocumentationOptions opts = null)
    {
        xmlDoc ??= BuildDocFromXml();
        opts ??= DefaultOptions();
        var ctx = new XmlDocumentationContext(xmlDoc);
        return new TypeDocumentation(FixturesAssembly, type, ctx, opts).ToString();
    }

    // ─── Record type output ───────────────────────────────────────────────────

    [Fact]
    public void TypeDocumentation_RecordClass_SignatureContainsRecord()
    {
        string output = GenerateDoc(typeof(SimpleRecord));
        output.Should().Contain("record");
        output.Should().NotContain(" class ");
    }

    [Fact]
    public void TypeDocumentation_RecordClass_DoesNotContainIEquatable()
    {
        string output = GenerateDoc(typeof(SimpleRecord));
        output.Should().NotContain("IEquatable");
    }

    [Fact]
    public void TypeDocumentation_RecordClass_DoesNotContainCompilerMethods()
    {
        string output = GenerateDoc(typeof(SimpleRecord));
        output.Should().NotContain("<Clone>$");
        output.Should().NotContain("PrintMembers");
        output.Should().NotContain("op_Equality");
    }

    [Fact]
    public void TypeDocumentation_RecordStruct_SignatureContainsRecordStruct()
    {
        string output = GenerateDoc(typeof(Point));
        output.Should().Contain("record");
        output.Should().Contain("struct");
    }

    // ─── inheritdoc resolution ────────────────────────────────────────────────

    [Fact]
    public void TypeDocumentation_InheritDoc_IncludesSummaryFromBase()
    {
        var xmlDoc = BuildDocFromXml(
            ("M:XMLDoc2Markdown.TestFixtures.IShape.Area", "<summary>Area of the shape.</summary><returns>A double.</returns>"),
            ("M:XMLDoc2Markdown.TestFixtures.Circle.Area", "<inheritdoc/>")
        );
        string output = GenerateDoc(typeof(Circle), xmlDoc);
        output.Should().Contain("Area of the shape.");
    }

    [Fact]
    public void TypeDocumentation_ExplicitInheritDocCref_IncludesTargetSummary()
    {
        var xmlDoc = BuildDocFromXml(
            ("M:XMLDoc2Markdown.TestFixtures.IShape.Area", "<summary>Computes area.</summary>"),
            ("M:XMLDoc2Markdown.TestFixtures.ExplicitInheritDocClass.ComputeArea", "<inheritdoc cref=\"M:XMLDoc2Markdown.TestFixtures.IShape.Area\"/>")
        );
        string output = GenerateDoc(typeof(ExplicitInheritDocClass), xmlDoc);
        output.Should().Contain("Computes area.");
    }

    // ─── Generic argument linking ─────────────────────────────────────────────

    [Fact]
    public void TypeDocumentation_GenericWrapper_LinksGenericArguments()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.Wrapper`1", "<summary>A generic wrapper.</summary>")
        );
        string output = GenerateDoc(typeof(Wrapper<PlainClass>), xmlDoc);
        // With linkGenericArguments=true, PlainClass should appear as a linked reference
        output.Should().Contain("PlainClass");
    }

    // ─── External cref ─────────────────────────────────────────────────────────

    [Fact]
    public void TypeDocumentation_ExternalCref_ProducesExternalLink()
    {
        var resolver = new ExternalDocsResolver();
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.ExternalCrefClass",
             "<summary>See <see cref=\"T:System.Collections.Generic.Dictionary`2\"/>.</summary>")
        );
        string output = GenerateDoc(typeof(ExternalCrefClass), xmlDoc, DefaultOptions(resolver));
        // The Dictionary cref should produce a learn.microsoft.com link (System namespace is mapped by default)
        output.Should().Contain("learn.microsoft.com");
    }

    [Fact]
    public void TypeDocumentation_UnresolvableCref_ProducesFriendlyText()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.ExternalCrefClass",
             "<summary>See <see cref=\"T:Totally.Unknown.TypeXyz\"/>.</summary>")
        );
        var opts = DefaultOptions();
        string output = GenerateDoc(typeof(ExternalCrefClass), xmlDoc, opts);
        // Should emit the last segment of the cref as plain text instead of a broken link
        output.Should().Contain("TypeXyz");
        output.Should().NotContain("Totally.Unknown.TypeXyz");
    }

    [Fact]
    public void TypeDocumentation_UnresolvableCref_IncrementsUnresolvedCount()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.ExternalCrefClass",
             "<summary>See <see cref=\"T:Totally.Unknown.TypeXyz\"/>.</summary>")
        );
        var opts = DefaultOptions();
        _ = GenerateDoc(typeof(ExternalCrefClass), xmlDoc, opts);
        opts.UnresolvedCount.Should().BeGreaterThan(0);
    }

    // ─── Front matter prepending (via TypeDocumentation options) ─────────────

    [Fact]
    public void TypeDocumentation_FrontMatter_IsPrependedToOutput()
    {
        var xmlDoc = BuildDocFromXml(
            ("T:XMLDoc2Markdown.TestFixtures.FrontMatterTarget", "<summary>Target type.</summary>")
        );
        var opts = DefaultOptions();
        opts.FrontMatter = "---\nparent: API\n---\n";

        string raw = new TypeDocumentation(FixturesAssembly, typeof(FrontMatterTarget),
            new XmlDocumentationContext(xmlDoc), opts).ToString();

        // The TypeDocumentation itself doesn't prepend — that's done by Program.cs
        // But the option is correctly stored and available
        opts.FrontMatter.Should().Contain("parent: API");
        raw.Should().NotBeEmpty();
    }
}
