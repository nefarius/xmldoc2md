using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using XMLDoc2Markdown.Utils;

namespace XMLDoc2Markdown;

/// <summary>Parses a single XML documentation file and exposes member lookup.</summary>
internal class XmlDocumentation
{
    public XmlDocumentation(string dllPath)
    {
        string xmlPath = Path.Combine(Directory.GetParent(dllPath)!.FullName,
            Path.GetFileNameWithoutExtension(dllPath) + ".xml");

        if (!File.Exists(xmlPath))
        {
            throw new FileNotFoundException(
                $"Could not load XML documentation file '{Path.GetFullPath(xmlPath)}'. File not found.", xmlPath);
        }

        try
        {
            XDocument xDocument = XDocument.Parse(File.ReadAllText(xmlPath));

            this.AssemblyName = xDocument.Descendants("assembly").First().Elements("name").First().Value;
            this.Members = xDocument.Descendants("members").First().Elements("member");
        }
        catch (Exception e)
        {
            throw new Exception("Unable to parse XML documentation", e);
        }
    }

    /// <summary>For in-memory construction during tests.</summary>
    internal XmlDocumentation(string assemblyName, IEnumerable<XElement> members)
    {
        this.AssemblyName = assemblyName;
        this.Members = members;
    }

    public string AssemblyName { get; }
    internal IEnumerable<XElement> Members { get; }

    public XElement GetMember(MemberInfo memberInfo)
    {
        return this.GetMember($"{memberInfo.MemberType.GetAlias()}:{memberInfo.GetIdentifier()}");
    }

    internal XElement GetMember(string name)
    {
        return this.Members.FirstOrDefault(member => member.Attribute("name")?.Value == name);
    }
}

/// <summary>
/// Aggregates a primary XML documentation file with zero-or-more secondary XML files
/// auto-discovered next to the input DLL (and any additional directories registered via
/// <see cref="AddSearchDirectory"/>). Enables cross-assembly <c>cref</c> and
/// <c>inheritdoc</c> resolution.
/// </summary>
internal class XmlDocumentationContext
{
    private readonly XmlDocumentation _primary;
    private readonly List<XmlDocumentation> _secondary = new();

    public XmlDocumentationContext(XmlDocumentation primary)
    {
        this._primary = primary;
    }

    /// <summary>The assembly name of the primary (target) documentation file.</summary>
    public string AssemblyName => this._primary.AssemblyName;

    /// <summary>Primary documentation instance — used for same-assembly lookups.</summary>
    internal XmlDocumentation Primary => this._primary;

    /// <summary>
    /// Scans <paramref name="directory"/> for *.xml files, parses each one that has a valid
    /// <c>&lt;assembly&gt;</c> element, and registers it as a secondary source.  XML files
    /// that belong to the primary assembly are skipped.
    /// </summary>
    public void AddSearchDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string xmlFile in Directory.EnumerateFiles(directory, "*.xml", SearchOption.TopDirectoryOnly))
        {
            try
            {
                XDocument doc = XDocument.Parse(File.ReadAllText(xmlFile));
                string assemblyName = doc.Descendants("assembly").FirstOrDefault()?.Elements("name").FirstOrDefault()?.Value;
                if (string.IsNullOrWhiteSpace(assemblyName) || assemblyName == this._primary.AssemblyName)
                {
                    continue;
                }

                // Avoid duplicates
                if (this._secondary.Any(s => s.AssemblyName == assemblyName))
                {
                    continue;
                }

                IEnumerable<XElement> members = doc.Descendants("members").First().Elements("member");
                this._secondary.Add(new XmlDocumentation(assemblyName, members));
            }
            catch
            {
                // Silently ignore malformed XML files in search directories
            }
        }
    }

    /// <summary>Looks up a member by its documentation ID in all registered XML files.</summary>
    /// <param name="id">The full documentation ID (e.g. <c>T:MyNamespace.MyClass</c>).</param>
    /// <param name="member">The matched XML element, or <c>null</c>.</param>
    /// <param name="sourceAssemblyName">The assembly that owns the match, or <c>null</c>.</param>
    /// <returns><c>true</c> if a match was found.</returns>
    public bool TryGetMember(string id, out XElement member, out string sourceAssemblyName)
    {
        XElement found = this._primary.GetMember(id);
        if (found != null)
        {
            member = found;
            sourceAssemblyName = this._primary.AssemblyName;
            return true;
        }

        foreach (XmlDocumentation secondary in this._secondary)
        {
            found = secondary.GetMember(id);
            if (found != null)
            {
                member = found;
                sourceAssemblyName = secondary.AssemblyName;
                return true;
            }
        }

        member = null;
        sourceAssemblyName = null;
        return false;
    }

    /// <summary>Convenience wrapper: looks up a <see cref="MemberInfo"/> in all sources.</summary>
    public XElement GetMember(MemberInfo memberInfo)
    {
        string id = $"{memberInfo.MemberType.GetAlias()}:{memberInfo.GetIdentifier()}";
        this.TryGetMember(id, out XElement xml, out _);
        return xml;
    }

    /// <summary>Looks up a raw documentation ID across all XML sources.</summary>
    public XElement GetMemberById(string id)
    {
        this.TryGetMember(id, out XElement member, out _);
        return member;
    }
}
