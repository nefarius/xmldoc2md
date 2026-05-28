using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using XMLDoc2Markdown.Utils;
using Xunit;

namespace XMLDoc2Markdown.Tests.Utils;

public class TypeExtensionsTests
{
    // ─── IsRecord ──────────────────────────────────────────────────────────────

    [Fact]
    public void IsRecord_ReturnsTrueForRecordClass()
    {
        typeof(SimpleRecord).IsRecord().Should().BeTrue();
    }

    [Fact]
    public void IsRecord_ReturnsTrueForRecordStruct()
    {
        typeof(Point).IsRecord().Should().BeTrue();
    }

    [Fact]
    public void IsRecord_ReturnsFalseForPlainClass()
    {
        typeof(PlainClass).IsRecord().Should().BeFalse();
    }

    [Fact]
    public void IsRecord_ReturnsFalseForString()
    {
        typeof(string).IsRecord().Should().BeFalse();
    }

    // ─── GetSignature (record keyword) ────────────────────────────────────────

    [Fact]
    public void GetSignature_EmitsRecordKeywordForRecordClass()
    {
        string sig = typeof(SimpleRecord).GetSignature(full: true);
        sig.Should().Contain("record");
        sig.Should().NotContain(" class ");
    }

    [Fact]
    public void GetSignature_EmitsRecordStructForRecordStruct()
    {
        string sig = typeof(Point).GetSignature(full: true);
        sig.Should().Contain("record");
        sig.Should().Contain("struct");
    }

    [Fact]
    public void GetSignature_EmitsClassForPlainClass()
    {
        string sig = typeof(PlainClass).GetSignature(full: true);
        sig.Should().Contain("class");
        sig.Should().NotContain("record");
    }

    // ─── GetCleanFullName (no assembly-qualified generic args) ────────────────

    [Fact]
    public void GetCleanFullName_NonGenericType_ReturnsFullName()
    {
        typeof(PlainClass).GetCleanFullName().Should().Be("XMLDoc2Markdown.TestFixtures.PlainClass");
    }

    [Fact]
    public void GetCleanFullName_GenericType_DoesNotContainSquareBrackets()
    {
        // IEquatable<SimpleRecord> FullName contains assembly-qualified args — GetCleanFullName should not
        Type ieq = typeof(IEquatable<SimpleRecord>);
        string clean = ieq.GetCleanFullName();
        clean.Should().NotContain("[[");
        clean.Should().NotContain("PublicKeyToken");
        clean.Should().Contain("SimpleRecord");
    }

    // ─── GetSignature does not emit assembly-qualified IEquatable for records ─

    [Fact]
    public void GetSignature_Record_DoesNotEmitAssemblyQualifiedEquatable()
    {
        string sig = typeof(SimpleRecord).GetSignature(full: true);
        sig.Should().NotContain("PublicKeyToken");
        sig.Should().NotContain("[[");
    }

    [Fact]
    public void GetSignature_Record_DoesNotEmitIEquatable()
    {
        string sig = typeof(SimpleRecord).GetSignature(full: true);
        // IEquatable<SimpleRecord> should be stripped from the record's implements list
        sig.Should().NotContain("IEquatable");
    }

    // ─── GetDisplayName ────────────────────────────────────────────────────────

    [Fact]
    public void GetDisplayName_GenericType_UsesPrettyName()
    {
        typeof(List<string>).GetDisplayName().Should().Be("List<String>");
    }

    [Fact]
    public void GetDisplayName_NonGeneric_UsesName()
    {
        typeof(PlainClass).GetDisplayName().Should().Be("PlainClass");
    }

    // ─── GetDocsLink (external resolver) ─────────────────────────────────────

    [Fact]
    public void GetDocsLink_SystemType_ReturnsLinkToMicrosoftDocs()
    {
        Assembly asm = Assembly.GetAssembly(typeof(PlainClass));
        var link = typeof(string).GetDocsLink(asm);
        link.ToString().Should().Contain("learn.microsoft.com");
    }

    [Fact]
    public void GetDocsLink_ExternalType_WithResolver_ReturnsExternalLink()
    {
        Assembly asm = Assembly.GetAssembly(typeof(PlainClass));
        var resolver = new ExternalDocsResolver();
        resolver.AddMapping("Xunit", "https://xunit.net/docs/api");

        var link = typeof(FactAttribute).GetDocsLink(asm, externalDocsResolver: resolver);
        link.ToString().Should().Contain("xunit.net");
    }

    // ─── GetDocsLink (generic argument linking) ────────────────────────────────

    [Fact]
    public void GetDocsLink_WithLinkGenericArguments_ProducesCompositeText()
    {
        Assembly asm = Assembly.GetAssembly(typeof(PlainClass));
        var link = typeof(List<PlainClass>).GetDocsLink(asm, linkGenericArguments: true);
        string rendered = link.ToString();
        // Should contain both "List" link and "PlainClass" link in the output
        rendered.Should().Contain("PlainClass");
        rendered.Should().Contain("List");
    }
}
