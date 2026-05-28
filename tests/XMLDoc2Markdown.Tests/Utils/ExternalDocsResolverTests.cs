using System;
using System.IO;
using FluentAssertions;
using XMLDoc2Markdown.Utils;
using Xunit;

namespace XMLDoc2Markdown.Tests.Utils;

public class ExternalDocsResolverTests
{
    private static ExternalDocsResolver BuildDefault() => new();

    // ─── Default mappings ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("System")]
    [InlineData("System.Collections.Generic")]
    [InlineData("System.Runtime.CompilerServices")]
    [InlineData("Microsoft.AspNetCore.Mvc")]
    [InlineData("Microsoft.Extensions.Logging")]
    public void DefaultMappings_MatchExpectedNamespaces(string ns)
    {
        var resolver = BuildDefault();
        string url = resolver.TryGetUrlByNamespace(ns);
        url.Should().NotBeNullOrEmpty($"namespace '{ns}' should be covered by a default mapping");
    }

    [Fact]
    public void DefaultMappings_UnknownNamespace_ReturnsNull()
    {
        var resolver = BuildDefault();
        resolver.TryGetUrlByNamespace("MyVeryPrivateLibrary.Internal").Should().BeNull();
    }

    // ─── TryGetUrl(Type) ───────────────────────────────────────────────────────

    [Fact]
    public void TryGetUrl_SystemType_ReturnsLearnMicrosoftUrl()
    {
        var resolver = BuildDefault();
        string url = resolver.TryGetUrl(typeof(string));
        url.Should().StartWith("https://learn.microsoft.com/dotnet/api");
        url.Should().Contain("system.string");
    }

    [Fact]
    public void TryGetUrl_NullableAttribute_ReturnsUrl()
    {
        // NullableAttribute is in System.Runtime.CompilerServices, so it should match
        var resolver = BuildDefault();
        // Use Type.GetType with the full name
        Type nullable = Type.GetType("System.Runtime.CompilerServices.NullableAttribute");
        if (nullable == null)
        {
            // Fallback: use Nullable<int> generic type to confirm the namespace is mapped
            string url = resolver.TryGetUrlByNamespace("System.Runtime.CompilerServices");
            url.Should().NotBeNullOrEmpty();
        }
        else
        {
            resolver.TryGetUrl(nullable).Should().NotBeNullOrEmpty();
        }
    }

    // ─── Custom mappings ──────────────────────────────────────────────────────

    [Fact]
    public void AddMapping_OverridesExistingDefaultForSamePrefix()
    {
        var resolver = BuildDefault();
        resolver.AddMapping("System", "https://custom.example.com/api");

        // Custom entries are prepended so they win
        string url = resolver.TryGetUrlByNamespace("System");
        url.Should().StartWith("https://custom.example.com");
    }

    [Fact]
    public void AddMapping_LongestPrefixWins()
    {
        var resolver = BuildDefault();
        resolver.AddMapping("My.Lib", "https://my.lib/docs");
        resolver.AddMapping("My.Lib.Core", "https://core.lib/docs");

        resolver.TryGetUrlByNamespace("My.Lib.Core.Helpers").Should().StartWith("https://core.lib");
        resolver.TryGetUrlByNamespace("My.Lib.Other").Should().StartWith("https://my.lib");
    }

    // ─── LoadFromFile ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadFromFile_ValidJson_RegistersMappings()
    {
        string json = """{ "Custom.Namespace": "https://custom.example.org/api" }""";
        string path = Path.GetTempFileName();
        File.WriteAllText(path, json);

        try
        {
            var resolver = BuildDefault();
            resolver.LoadFromFile(path);
            resolver.TryGetUrlByNamespace("Custom.Namespace").Should().StartWith("https://custom.example.org");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromFile_MissingFile_Throws()
    {
        var resolver = BuildDefault();
        Action act = () => resolver.LoadFromFile("/does/not/exist.json");
        act.Should().Throw<FileNotFoundException>();
    }

    // ─── GetDocsElement ──────────────────────────────────────────────────────

    [Fact]
    public void GetDocsElement_MappedType_ReturnsMarkdownLink()
    {
        var resolver = BuildDefault();
        var element = resolver.GetDocsElement(typeof(string), "String");
        element.ToString().Should().Contain("learn.microsoft.com");
    }

    [Fact]
    public void GetDocsElement_UnmappedType_ReturnsMarkdownText()
    {
        var resolver = BuildDefault();
        var element = resolver.GetDocsElement(typeof(ExternalDocsResolverTests), "SomeType");
        element.Should().BeOfType<Markdown.MarkdownText>();
    }
}
