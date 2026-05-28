using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Markdown;

namespace XMLDoc2Markdown.Utils;

/// <summary>
/// Maps namespace prefixes to URL templates for external documentation.
/// Used to produce hyperlinks for types that live outside the documented assembly
/// (e.g. <c>System.*</c> → learn.microsoft.com).
/// </summary>
internal class ExternalDocsResolver
{
    private const string MicrosoftLearnBase = "https://learn.microsoft.com/dotnet/api";

    /// <summary>
    /// Default namespace-prefix → URL-base mappings that ship with the tool.
    /// The longest matching prefix wins.
    /// </summary>
    private static readonly IReadOnlyList<(string Prefix, string UrlBase)> DefaultMappings =
    [
        ("System", MicrosoftLearnBase),
        ("Microsoft.AspNetCore", MicrosoftLearnBase),
        ("Microsoft.Extensions", MicrosoftLearnBase),
        ("Microsoft.EntityFrameworkCore", MicrosoftLearnBase),
        ("Microsoft.CSharp", MicrosoftLearnBase),
        ("Microsoft.Win32", MicrosoftLearnBase),
    ];

    private readonly List<(string Prefix, string UrlBase)> _mappings;

    public ExternalDocsResolver()
    {
        this._mappings = new List<(string, string)>(DefaultMappings);
    }

    /// <summary>
    /// Parses a <c>namespace=urlBase</c> pair (from the CLI <c>--external-docs</c> option)
    /// and registers it with highest priority.
    /// </summary>
    public void AddMapping(string namespacePrefix, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(namespacePrefix) || string.IsNullOrWhiteSpace(urlBase))
        {
            return;
        }

        // Prepend so user-supplied entries win over defaults
        this._mappings.Insert(0, (namespacePrefix.Trim(), urlBase.TrimEnd('/')));
    }

    /// <summary>Parses a JSON file of the form <c>{ "System": "https://...", ... }</c>.</summary>
    public void LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"External docs mapping file not found: {path}", path);
        }

        string json = File.ReadAllText(path);
        Dictionary<string, string> entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? throw new InvalidOperationException($"Failed to parse external docs file: {path}");

        foreach (KeyValuePair<string, string> kv in entries)
        {
            this.AddMapping(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Tries to resolve a documentation URL for <paramref name="type"/>.
    /// Returns <c>null</c> when no mapping matches.
    /// </summary>
    public string TryGetUrl(Type type)
    {
        if (type == null)
        {
            return null;
        }

        string ns = type.Namespace ?? string.Empty;

        // Longest-prefix match
        string urlBase = this._mappings
            .OrderByDescending(m => m.Prefix.Length)
            .Where(m => ns == m.Prefix || ns.StartsWith(m.Prefix + ".", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.UrlBase)
            .FirstOrDefault();

        if (urlBase == null)
        {
            return null;
        }

        // Build the doc page slug: dotted lower-case type name (generic backtick removed)
        string slug = BuildSlug(type);
        return $"{urlBase}/{slug}";
    }

    /// <summary>
    /// Returns a <see cref="MarkdownInlineElement"/> for <paramref name="type"/>:
    /// a hyperlink when a mapping exists, otherwise a plain text label.
    /// </summary>
    public MarkdownInlineElement GetDocsElement(Type type, string displayText)
    {
        string url = this.TryGetUrl(type);
        return url != null
            ? (MarkdownInlineElement)new MarkdownLink(displayText, url)
            : new MarkdownText(displayText);
    }

    /// <summary>
    /// Returns the URL base for <paramref name="namespaceName"/> using the longest-prefix match,
    /// or <c>null</c> when no mapping matches.  Useful when only the namespace string is known
    /// (e.g. from a cref attribute) and no <see cref="Type"/> is available.
    /// </summary>
    public string TryGetUrlByNamespace(string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return null;
        }

        return this._mappings
            .OrderByDescending(m => m.Prefix.Length)
            .Where(m => namespaceName == m.Prefix ||
                        namespaceName.StartsWith(m.Prefix + ".", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.UrlBase)
            .FirstOrDefault();
    }

    private static string BuildSlug(Type type)
    {
        if (type == null)
        {
            return string.Empty;
        }

        if (type.IsGenericType)
        {
            // e.g. System.Collections.Generic.List`1 → system.collections.generic.list-1
            string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
            return name.ToLowerInvariant().Replace('`', '-').Replace('+', '.');
        }

        return (type.FullName ?? type.Name).ToLowerInvariant().Replace('+', '.');
    }
}
