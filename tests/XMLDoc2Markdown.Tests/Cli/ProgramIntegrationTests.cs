using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace XMLDoc2Markdown.Tests.Cli;

/// <summary>
/// Smoke tests for the full CLI pipeline: builds documentation for the
/// <c>XMLDoc2Markdown.TestFixtures</c> assembly and asserts on the output files.
/// </summary>
public class ProgramIntegrationTests
{
    private static string FixturesDllPath =>
        typeof(XMLDoc2Markdown.TestFixtures.SimpleRecord).Assembly.Location;

    // Serialise console redirection so parallel tests don't stomp each other.
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Invokes Program.Main via reflection, capturing stdout/stderr.
    /// Throws <see cref="InvalidOperationException"/> when the CLI exits non-zero.
    /// </summary>
    private static string RunCli(string[] args)
    {
        lock (ConsoleLock)
        {
            var writer = new StringWriter();
            TextWriter stdOut = Console.Out;
            TextWriter stdErr = Console.Error;
            Console.SetOut(writer);
            Console.SetError(writer);
            int exit;
            try
            {
                exit = (int)typeof(Program)
                    .GetMethod("Main", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [args])!;
            }
            finally
            {
                Console.SetOut(stdOut);
                Console.SetError(stdErr);
            }

            if (exit != 0)
            {
                throw new InvalidOperationException(
                    $"CLI exited with code {exit}. Output:\n{writer}");
            }

            return writer.ToString();
        }
    }

    private static (string output, string[] files) RunToDir(params string[] extraArgs)
    {
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            string[] args = [FixturesDllPath, outDir, .. extraArgs];
            string output = RunCli(args);
            string[] files = Directory.GetFiles(outDir, "*.md", SearchOption.TopDirectoryOnly);
            // Copy results and clean up
            return (output, files);
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── Basic smoke ─────────────────────────────────────────────────────────

    [Fact]
    public void Run_BasicGeneration_ProducesMarkdownFiles()
    {
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            RunCli([FixturesDllPath, outDir]);
            Directory.GetFiles(outDir, "*.md").Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── Front matter ─────────────────────────────────────────────────────────

    [Fact]
    public void Run_WithFrontMatter_PrependsFrontMatterToTypePage()
    {
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            string fm = "---\nparent: Tests\n---\n";
            string fmFile = Path.GetTempFileName();
            File.WriteAllText(fmFile, fm);
            try
            {
                RunCli([FixturesDllPath, outDir, $"--front-matter=@{fmFile}"]);
                string[] mdFiles = Directory.GetFiles(outDir, "*.md");
                mdFiles.Should().NotBeEmpty();

                // Every type page (not index) should start with the front matter
                foreach (string mdFile in mdFiles)
                {
                    if (Path.GetFileNameWithoutExtension(mdFile) == "index")
                    {
                        continue;
                    }

                    string content = File.ReadAllText(mdFile);
                    content.Should().StartWith("---", $"file {mdFile} should have YAML front matter prepended");
                }
            }
            finally
            {
                File.Delete(fmFile);
            }
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void Run_FrontMatterPlaceholders_AreSubstituted()
    {
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            RunCli([FixturesDllPath, outDir, "--front-matter=---\nassembly: {AssemblyName}\n---\n"]);
            string[] mdFiles = Directory.GetFiles(outDir, "*.md");
            mdFiles.Should().NotBeEmpty();

            // At least one type file should have the assembly name substituted
            bool found = false;
            foreach (string mdFile in mdFiles)
            {
                if (Path.GetFileNameWithoutExtension(mdFile) == "index")
                {
                    continue;
                }

                string content = File.ReadAllText(mdFile);
                if (content.Contains("XMLDoc2Markdown.TestFixtures"))
                {
                    found = true;
                    break;
                }
            }

            found.Should().BeTrue("at least one type page should have the assembly name substituted");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── External docs ───────────────────────────────────────────────────────

    [Fact]
    public void Run_WithExternalDocs_MapIsApplied()
    {
        const string customBase = "https://example.docs.test/api";

        // Write a mapping file: map "System" to our custom base URL so we can
        // assert the URL appears in the generated output.
        string mapFile = Path.GetTempFileName();
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            File.WriteAllText(mapFile, $"{{\"System\":\"{customBase}\"}}");

            // ExternalCrefClass has a <see cref="System.Collections.Generic.Dictionary{...}"/>
            // which should be resolved via the mapping.
            RunCli([FixturesDllPath, outDir, $"--external-docs-file={mapFile}"]);

            string[] mdFiles = Directory.GetFiles(outDir, "*.md");
            mdFiles.Should().NotBeEmpty();

            bool foundLink = mdFiles.Any(f => File.ReadAllText(f).Contains(customBase));
            foundLink.Should().BeTrue("at least one generated file should contain the external docs base URL");
        }
        finally
        {
            File.Delete(mapFile);
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── Record types ─────────────────────────────────────────────────────────

    [Fact]
    public void Run_RecordType_OutputContainsRecordKeyword()
    {
        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            RunCli([FixturesDllPath, outDir]);
            string recordFile = Path.Combine(outDir, "xmldoc2markdown.testfixtures.simplerecord.md");
            File.Exists(recordFile).Should().BeTrue($"expected page '{recordFile}' to be generated");
            string content = File.ReadAllText(recordFile);
            content.Should().Contain("record");
            content.Should().NotContain("IEquatable");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }
}
