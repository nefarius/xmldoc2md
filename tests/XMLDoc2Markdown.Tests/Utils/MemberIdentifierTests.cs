using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using XMLDoc2Markdown.Utils;
using Xunit;

namespace XMLDoc2Markdown.Tests.Utils;

public class MemberIdentifierTests
{
    private static readonly HashSet<string> CompilerIds = LoadCompilerIds();

    [Theory]
    [MemberData(nameof(DocumentedMethods))]
    public void GetIdentifier_MatchesCompilerDocumentationId(MethodBase method)
    {
        CompilerIds.Should().Contain("M:" + method.GetIdentifier());
    }

    public static IEnumerable<object[]> DocumentedMethods()
    {
        yield return new object[] { typeof(OpenResult<>).GetMethod(nameof(OpenResult<>.Ok)) };
        yield return new object[] { typeof(OpenResult<>).GetMethod(nameof(OpenResult<>.Fail)) };
        yield return new object[] { typeof(OpenResult<>).GetConstructors().Single() };
        yield return new object[] { typeof(OuterBox<>.InnerBox<>).GetMethod(nameof(OuterBox<>.InnerBox<>.Use)) };
        yield return new object[] { typeof(OuterBox<>.PlainInner).GetMethod(nameof(OuterBox<>.PlainInner.Use)) };
        yield return new object[] { typeof(GenericMethodBox<>).GetMethod(nameof(GenericMethodBox<>.Accept)) };
        yield return new object[] { typeof(ShadowedBox<>).GetMethod(nameof(ShadowedBox<>.Accept)) };
    }

    [Fact]
    public void GetIdentifier_ConstructedInterfaceMethod_UsesGenericDefinition()
    {
        Type equatable = typeof(IEquatable<>).MakeGenericType(typeof(OpenResult<>));
        MethodInfo equals = equatable.GetMethod(nameof(IEquatable<>.Equals));

        equals.GetIdentifier().Should().Be("System.IEquatable`1.Equals(`0)");
    }

    private static HashSet<string> LoadCompilerIds()
    {
        string xmlPath = Path.ChangeExtension(typeof(OpenResult<>).Assembly.Location, ".xml");
        return XDocument.Load(xmlPath)
            .Descendants("member")
            .Select(member => member.Attribute("name")?.Value)
            .Where(name => name != null)
            .ToHashSet();
    }
}
