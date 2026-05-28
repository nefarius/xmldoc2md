using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using VerifyTests;
using VerifyXunit;
using Xunit;

namespace XMLDoc2Markdown.Tests;

/// <summary>
/// Real-world regression tests that run the generator over the pinned
/// <c>Nefarius.Utilities.ETW</c> NuGet package and lock the full output
/// behind a Verify golden snapshot.
/// </summary>
public class RealWorldEtwSnapshotTests
{
    /// <summary>
    /// The pinned ETW package version, injected via AssemblyMetadata from the csproj.
    /// </summary>
    private static readonly string EtwVersion =
        typeof(RealWorldEtwSnapshotTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "EtwSampleVersion")
            .Value!;

    /// <summary>
    /// Resolves the NuGet global packages folder — NUGET_PACKAGES env var if set
    /// (and non-empty/non-whitespace), else the default per-user cache (~/.nuget/packages).
    /// </summary>
    private static string NuGetPackagesRoot
    {
        get
        {
            string nugetEnvValue = Environment.GetEnvironmentVariable("NUGET_PACKAGES")?.Trim();
            return !string.IsNullOrWhiteSpace(nugetEnvValue)
                ? nugetEnvValue
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        }
    }

    /// <summary>
    /// Absolute path to the ETW DLL inside the NuGet global cache.
    /// </summary>
    private static string EtwDllPath =>
        Path.Combine(NuGetPackagesRoot, "nefarius.utilities.etw", EtwVersion,
            "lib", "net9.0-windows8.0", "Nefarius.Utilities.ETW.dll");

    /// <summary>
    /// Thread-safe cache for the combined Markdown output.  The generation is expensive
    /// (spawns the full CLI pipeline) so we compute it once per test run and reuse.
    /// </summary>
    private static readonly Lazy<string> CombinedDocs = new(ComputeCombinedDocs, isThreadSafe: true);

    // Serialise console redirection so parallel tests don't stomp each other.
    private static readonly object ConsoleLock = new();

    /// <summary>
    /// Invokes <c>Program.Main</c> via reflection with the given args.
    /// Throws when the CLI exits non-zero.
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

    /// <summary>
    /// Returns the cached combined Markdown output, computing it on first call.
    /// Tests call this method so they remain readable and any future change to
    /// the caching strategy only requires touching this one place.
    /// </summary>
    private static string GenerateCombinedDocs() => CombinedDocs.Value;

    /// <summary>
    /// Cold-path worker — called at most once by the <see cref="CombinedDocs"/> lazy.
    /// Generates docs for the ETW assembly and returns the combined Markdown
    /// (one file per type, sorted by filename, each prefixed with a header line).
    /// </summary>
    private static string ComputeCombinedDocs()
    {
        string dllPath = EtwDllPath;

        if (!File.Exists(dllPath))
        {
            Assert.Skip(
                $"Nefarius.Utilities.ETW {EtwVersion} not found at expected NuGet path: {dllPath}. " +
                "Run 'dotnet restore' to populate the global packages cache.");
        }

        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outDir);
        try
        {
            RunCli([dllPath, outDir]);

            string[] mdFiles = Directory.GetFiles(outDir, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            mdFiles.Should().NotBeEmpty("ETW assembly should generate at least one Markdown page");

            // Combine into a single artifact: each file preceded by a separator header
            var sb = new System.Text.StringBuilder();
            foreach (string file in mdFiles)
            {
                sb.AppendLine($"===== {Path.GetFileName(file)} =====");
                sb.AppendLine(File.ReadAllText(file));
            }

            return sb.ToString();
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
        }
    }

    // ─── Golden snapshot ──────────────────────────────────────────────────────

    [Fact]
    public Task EtwAllDocs_MatchesGoldenSnapshot()
    {
        string combined = GenerateCombinedDocs();
        var settings = new VerifySettings();
        settings.UseDirectory("Snapshots");
        settings.UseFileName("Etw.AllDocs");
        return Verifier.Verify(combined, settings);
    }

    // ─── Anti-regression assertions ───────────────────────────────────────────

    [Fact]
    public void EtwAllDocs_NoEmptyInlineReferenceGaps()
    {
        string combined = GenerateCombinedDocs();

        // These patterns indicate a broken inline reference rendering:
        //   "sessionName is ."  -> <see langword="null"/> rendered as empty
        //   "with ; "           -> <paramref> or inline ref lost the target
        combined.Should().NotMatchRegex(@" is \.",
            "langword='null' must render as `null`, not produce 'is .' gap");
        combined.Should().NotMatchRegex(@"with ;\s",
            "paramref/inline-refs must not produce 'with ;' gap");
    }

    [Fact]
    public void EtwAllDocs_ContainsLearnMicrosoftComLinks()
    {
        string combined = GenerateCombinedDocs();

        // All Microsoft API links must use learn.microsoft.com (not the old docs.microsoft.com)
        combined.Should().Contain("learn.microsoft.com",
            "external MS API links should use learn.microsoft.com");
        combined.Should().NotContain("docs.microsoft.com",
            "old docs.microsoft.com links should have been migrated to learn.microsoft.com");
    }

    [Fact]
    public void EtwAllDocs_LangwordRendersAsInlineCode()
    {
        string combined = GenerateCombinedDocs();

        // At least one langword should render as inline code
        combined.Should().MatchRegex(@"`(null|true|false|await|async)`",
            "langword attributes must render as inline code spans");
    }
}
