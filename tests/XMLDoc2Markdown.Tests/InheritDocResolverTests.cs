using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using Xunit;

namespace XMLDoc2Markdown.Tests;

public class InheritDocResolverTests
{
    private static XmlDocumentationContext BuildContext(params (string id, string xml)[] members)
    {
        var elements = members.Select(m => XElement.Parse($"""<member name="{m.id}">{m.xml}</member>"""));
        var doc = new XmlDocumentation("TestAssembly", elements);
        return new XmlDocumentationContext(doc);
    }

    // ─── Basic inheritdoc from base class ─────────────────────────────────────

    [Fact]
    public void Resolve_MemberWithInheritDoc_ReturnsBaseClassSummary()
    {
        // Circle.Area() has <inheritdoc/>, BaseShape.Area() has <summary>
        var elements = new[]
        {
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.BaseShape.Area"><summary>Base area summary.</summary></member>"""),
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.Circle.Area"><inheritdoc/></member>"""),
        };
        var doc = new XmlDocumentation("TestAssembly", elements);
        var ctx = new XmlDocumentationContext(doc);

        MethodInfo method = typeof(Circle).GetMethod("Area");
        XElement result = InheritDocResolver.Resolve(method, ctx);

        result.Should().NotBeNull();
        result.Element("summary")?.Value.Should().Be("Base area summary.");
    }

    [Fact]
    public void Resolve_MemberWithInheritDoc_ReturnsInterfaceSummary()
    {
        // Circle.Area() inherits from IShape.Area() if BaseShape.Area() has no docs
        var elements = new[]
        {
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.IShape.Area"><summary>Interface area summary.</summary></member>"""),
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.Circle.Area"><inheritdoc/></member>"""),
        };
        var doc = new XmlDocumentation("TestAssembly", elements);
        var ctx = new XmlDocumentationContext(doc);

        MethodInfo method = typeof(Circle).GetMethod("Area");
        XElement result = InheritDocResolver.Resolve(method, ctx);

        result.Should().NotBeNull();
        result.Element("summary")?.Value.Should().Be("Interface area summary.");
    }

    // ─── Explicit cref ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_ExplicitCref_ReturnsTargetMemberDoc()
    {
        var elements = new[]
        {
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.IShape.Area"><summary>Shape area from interface.</summary></member>"""),
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.ExplicitInheritDocClass.ComputeArea"><inheritdoc cref="M:XMLDoc2Markdown.TestFixtures.IShape.Area"/></member>"""),
        };
        var doc = new XmlDocumentation("TestAssembly", elements);
        var ctx = new XmlDocumentationContext(doc);

        MethodInfo method = typeof(ExplicitInheritDocClass).GetMethod("ComputeArea");
        XElement result = InheritDocResolver.Resolve(method, ctx);

        result.Should().NotBeNull();
        result.Element("summary")?.Value.Should().Be("Shape area from interface.");
    }

    // ─── No inheritdoc — return as-is ─────────────────────────────────────────

    [Fact]
    public void Resolve_NormalMember_ReturnsOriginalElement()
    {
        var elements = new[]
        {
            XElement.Parse("""<member name="M:XMLDoc2Markdown.TestFixtures.IShape.Area"><summary>Original summary.</summary></member>"""),
        };
        var doc = new XmlDocumentation("TestAssembly", elements);
        var ctx = new XmlDocumentationContext(doc);

        MethodInfo method = typeof(IShape).GetMethod("Area");
        XElement result = InheritDocResolver.Resolve(method, ctx);

        result.Should().NotBeNull();
        result.Element("summary")?.Value.Should().Be("Original summary.");
    }

    // ─── No doc at all ─────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NoDocumentationAnywhere_ReturnsNull()
    {
        var ctx = BuildContext(); // empty context
        MethodInfo method = typeof(Circle).GetMethod("Area");

        XElement result = InheritDocResolver.Resolve(method, ctx);
        result.Should().BeNull();
    }

    // ─── Cycle protection ────────────────────────────────────────────────────

    [Fact]
    public void Resolve_CircularInheritdoc_DoesNotStackOverflow()
    {
        // Build a self-referential cref cycle using a real MemberInfo so that
        // InheritDocResolver.Resolve is actually invoked (not just the context lookup).
        MethodInfo method = typeof(ExplicitInheritDocClass).GetMethod("ComputeArea")!;
        string id = $"M:{method.DeclaringType!.FullName}.{method.Name}";

        var elements = new[]
        {
            // The element inherits from itself — a direct cref cycle
            XElement.Parse($"""<member name="{id}"><inheritdoc cref="{id}"/></member>"""),
        };
        var doc = new XmlDocumentation("TestAssembly", elements);
        var ctx = new XmlDocumentationContext(doc);

        // Must not throw StackOverflowException or hang; resolver should detect the cycle
        // and return null rather than looping indefinitely.
        XElement result = InheritDocResolver.Resolve(method, ctx);
        result.Should().BeNull("the cycle should be detected and broken by the visited-set guard");
    }
}
