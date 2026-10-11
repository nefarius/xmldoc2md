namespace XMLDoc2Markdown.Tests.Cli;

/// <summary>Serializes CLI invocations that redirect the process console or the NuGet cache.</summary>
internal static class CliTestSync
{
    internal static readonly object ConsoleLock = new();
}
