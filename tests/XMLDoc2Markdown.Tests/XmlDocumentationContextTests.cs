using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace XMLDoc2Markdown.Tests;

public class XmlDocumentationContextTests
{
    private static XmlDocumentation BuildDoc(string assemblyName, string memberXml)
    {
        var members = new[]
        {
            XElement.Parse($"""<member name="{memberXml}"><summary>Summary for {memberXml}.</summary></member>""")
        };
        return new XmlDocumentation(assemblyName, members);
    }

    [Fact]
    public void TryGetMember_PrimaryDoc_ReturnsElement()
    {
        var primary = BuildDoc("PrimaryAssembly", "T:MyNamespace.MyClass");
        var ctx = new XmlDocumentationContext(primary);

        bool found = ctx.TryGetMember("T:MyNamespace.MyClass", out XElement member, out string source);

        found.Should().BeTrue();
        member.Should().NotBeNull();
        source.Should().Be("PrimaryAssembly");
    }

    [Fact]
    public void TryGetMember_SecondaryDoc_ReturnsElement()
    {
        var primary = BuildDoc("PrimaryAssembly", "T:MyNamespace.MyClass");
        var ctx = new XmlDocumentationContext(primary);

        // Manually register a secondary doc (simulating a neighbour XML file)
        var secondaryMembers = new[]
        {
            XElement.Parse("""<member name="T:External.SomeType"><summary>External summary.</summary></member>""")
        };
        var secondary = new XmlDocumentation("ExternalAssembly", secondaryMembers);
        typeof(XmlDocumentationContext)
            .GetField("_secondary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(ctx)!
            .GetType()
            .GetMethod("Add")!
            .Invoke(
                typeof(XmlDocumentationContext)
                    .GetField("_secondary", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(ctx),
                [secondary]);

        bool found = ctx.TryGetMember("T:External.SomeType", out XElement member, out string source);

        found.Should().BeTrue();
        source.Should().Be("ExternalAssembly");
    }

    [Fact]
    public void TryGetMember_NotFound_ReturnsFalse()
    {
        var primary = BuildDoc("PrimaryAssembly", "T:MyNamespace.MyClass");
        var ctx = new XmlDocumentationContext(primary);

        bool found = ctx.TryGetMember("T:Unknown.Type", out XElement member, out string source);

        found.Should().BeFalse();
        member.Should().BeNull();
        source.Should().BeNull();
    }

    [Fact]
    public void AddSearchDirectory_PopulatesSecondaryDocs()
    {
        var primary = BuildDoc("PrimaryAssembly", "T:MyNamespace.MyClass");
        var ctx = new XmlDocumentationContext(primary);

        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            // Create a valid secondary XML doc in the temp directory
            string xmlContent = """
                <?xml version="1.0"?>
                <doc>
                  <assembly><name>SecondaryAssembly</name></assembly>
                  <members>
                    <member name="T:SecondaryNS.SecondaryClass">
                      <summary>From secondary.</summary>
                    </member>
                  </members>
                </doc>
                """;
            File.WriteAllText(Path.Combine(dir, "SecondaryAssembly.xml"), xmlContent);

            ctx.AddSearchDirectory(dir);

            bool found = ctx.TryGetMember("T:SecondaryNS.SecondaryClass", out XElement member, out string source);
            found.Should().BeTrue();
            source.Should().Be("SecondaryAssembly");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddSearchDirectory_PrimaryAlreadyScanned_NotAddedAsSecondary()
    {
        var primary = BuildDoc("PrimaryAssembly", "T:MyNamespace.MyClass");
        var ctx = new XmlDocumentationContext(primary);

        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            // XML that has same assembly name as primary should be skipped
            string xmlContent = """
                <?xml version="1.0"?>
                <doc>
                  <assembly><name>PrimaryAssembly</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Duplicate">
                      <summary>Duplicate.</summary>
                    </member>
                  </members>
                </doc>
                """;
            File.WriteAllText(Path.Combine(dir, "PrimaryAssembly.xml"), xmlContent);
            ctx.AddSearchDirectory(dir);

            // Should NOT find the duplicate — the XML file has the same assembly name as the primary
            // and is therefore skipped by AddSearchDirectory to prevent double-registration.
            bool found = ctx.TryGetMember("T:MyNamespace.Duplicate", out _, out string source);
            found.Should().BeFalse("the file matching the primary assembly name must not be added as secondary");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
