using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using XMLDoc2Markdown.Utils;
using Xunit;

namespace XMLDoc2Markdown.Tests.Utils;

public class AssemblyResolverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("xmldoc2md-resolver").FullName;

    public void Dispose()
    {
        Directory.Delete(this._root, recursive: true);
    }

    [Fact]
    public void TryResolveAssemblyPath_PrefersNet10OverNetStandard()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "10.0.0", "netstandard2.0");
        this.WritePackage(assemblyName, "10.0.0", "net10.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_OrdersPackageVersionsSemantically()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "10.0.9", "net10.0");
        this.WritePackage(assemblyName, "10.0.12", "net10.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}10.0.12{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_SkipsHigherVersionWithoutCompatibleAssembly()
    {
        const string assemblyName = "Probe.Sample";
        string emptyVersion = Path.Combine(this._root, assemblyName.ToLowerInvariant(), "10.0.12", "ref", "net10.0");
        Directory.CreateDirectory(emptyVersion);
        this.WritePackage(assemblyName, "10.0.9", "net10.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}10.0.9{Path.DirectorySeparatorChar}");
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net6.0")]
    [InlineData("netcoreapp3.1")]
    [InlineData("net472")]
    [InlineData("net9.0-windows8.0")]
    public void TryResolveAssemblyPath_ResolvesOlderFrameworkAssets(string framework)
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "8.0.0", framework);

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=8.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}{framework}{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_PrefersNearestModernAssetOverNetStandard()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "8.0.0", "netstandard2.0");
        this.WritePackage(assemblyName, "8.0.0", "net8.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=8.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}net8.0{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_PrefersCompatibleAssetOverFrameworkOnly()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "8.0.0", "net472");
        this.WritePackage(assemblyName, "8.0.0", "netstandard2.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=8.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}netstandard2.0{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_PrefersNewestFrameworkAsset()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "4.8.0", "net462");
        this.WritePackage(assemblyName, "4.8.0", "net48");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=4.8.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}net48{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public void TryResolveAssemblyPath_PrefersPortableAssetOverWindowsTarget()
    {
        const string assemblyName = "Probe.Sample";
        this.WritePackage(assemblyName, "10.0.0", "net10.0-windows8.0");
        this.WritePackage(assemblyName, "10.0.0", "net10.0");

        string path = new AssemblyResolver(this._root)
            .TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"));

        path.Should().NotBeNull();
        path.Should().Contain($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}");
        path.Should().NotContain("windows");
    }

    [Fact]
    public void TryResolveAssemblyPath_ReturnsNoMatchForMissingOrWrongMajor()
    {
        const string assemblyName = "Probe.Sample";
        AssemblyResolver resolver = new(this._root);

        resolver.TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"))
            .Should().BeNull();

        this.WritePackage(assemblyName, "9.0.4", "net10.0");
        Directory.CreateDirectory(Path.Combine(this._root, assemblyName.ToLowerInvariant(), "not-a-version"));

        resolver.TryResolveAssemblyPath(new AssemblyName($"{assemblyName}, Version=10.0.0.0"))
            .Should().BeNull();
    }

    private void WritePackage(string assemblyName, string version, string framework)
    {
        string path = Path.Combine(
            this._root,
            assemblyName.ToLowerInvariant(),
            version,
            "lib",
            framework,
            assemblyName + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
    }
}
