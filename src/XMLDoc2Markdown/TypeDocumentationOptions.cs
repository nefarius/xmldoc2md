namespace XMLDoc2Markdown;

internal class TypeDocumentationOptions
{
    public string ExamplesDirectory { get; set; }
    public bool GitHubPages { get; set; }
    public string BackButton { get; set; }
    public string LinkBackButton { get; set; }
    public bool HasBackButton { get; set; }
    public bool GitlabWiki { get; set; }
    public bool IncludePrivateMembers { get; set; }
    public bool ExcludeInternals { get; set; }
    public bool OnlyInternalMembers { get; set; }
    public bool FoundBackButtonTemplate { get; set; }

    /// <summary>
    /// When <c>true</c> (default), each generic type argument in a type reference is rendered
    /// as its own hyperlink rather than being swallowed by the outer type link.
    /// </summary>
    public bool LinkGenericArguments { get; set; } = true;

    /// <summary>
    /// Front matter string prepended verbatim to every generated type page.
    /// Supports placeholders: <c>{TypeName}</c>, <c>{Namespace}</c>,
    /// <c>{AssemblyName}</c>, <c>{Date}</c>.
    /// </summary>
    public string FrontMatter { get; set; }

    /// <summary>Front matter string prepended to the index page only.</summary>
    public string IndexFrontMatter { get; set; }

    /// <summary>
    /// When <c>true</c>, unresolved types produce a logged warning but the run continues.
    /// This is the default behaviour (same as the original tool).
    /// </summary>
    public bool IgnoreUnresolvedTypes { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the tool exits with a non-zero code if any type reference
    /// could not be resolved.
    /// </summary>
    public bool FailOnUnresolved { get; set; }

    /// <summary>External docs URL resolver injected from <c>Program</c>.</summary>
    public Utils.ExternalDocsResolver ExternalDocsResolver { get; set; }

    /// <summary>
    /// Counter incremented by <c>TypeDocumentation</c> each time a cref cannot be resolved.
    /// Checked at the end of the run when <see cref="FailOnUnresolved"/> is set.
    /// </summary>
    public int UnresolvedCount { get; set; }
}
