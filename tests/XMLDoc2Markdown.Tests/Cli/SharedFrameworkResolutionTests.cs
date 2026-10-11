using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using XMLDoc2Markdown.TestFixtures;
using Xunit;

namespace XMLDoc2Markdown.Tests.Cli;

public class SharedFrameworkResolutionTests
{
    [Fact]
    public void Run_LoggingConsumer_ResolvesSharedFrameworkLogger()
    {
        string fixturePath = typeof(LoggingConsumer).Assembly.Location;
        string fixtureDir = Path.GetDirectoryName(fixturePath)!;
        File.Exists(Path.Combine(fixtureDir, "Microsoft.Extensions.Logging.Abstractions.dll"))
            .Should().BeFalse("the logger assembly must come from the shared framework, not copied output");

        string outDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string emptyPackages = Directory.CreateTempSubdirectory("xmldoc2md-empty-packages").FullName;
        Directory.CreateDirectory(outDir);
        string previousPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            string output;
            lock (CliTestSync.ConsoleLock)
            {
                Environment.SetEnvironmentVariable("NUGET_PACKAGES", emptyPackages);
                try
                {
                    output = RunCli([fixturePath, outDir, "--fail-on-unresolved"]);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousPackages);
                }
            }

            output.Should().NotContain("0x80131509");
            output.Should().NotContain("Could not load file or assembly 'Microsoft.Extensions.Logging.Abstractions");
            output.Should().Contain("0 failed");

            string page = Path.Combine(outDir, "xmldoc2markdown.testfixtures.loggingconsumer.md");
            File.Exists(page).Should().BeTrue();
            string content = File.ReadAllText(page);
            content.Should().Contain("LoggingConsumer");
            content.Should().Contain("ILogger");
        }
        finally
        {
            Directory.Delete(outDir, recursive: true);
            Directory.Delete(emptyPackages, recursive: true);
        }
    }

    private static string RunCli(string[] args)
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
