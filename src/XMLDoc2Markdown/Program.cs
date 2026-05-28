using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Markdown;
using McMaster.Extensions.CommandLineUtils;

using XMLDoc2Markdown.Utils;

namespace XMLDoc2Markdown;

internal class Program
{
    private static int Main(string[] args)
    {
        CommandLineApplication app = new()
        {
            Name = "xmldoc2md"
        };

        app.VersionOption("-v|--version", () => $"Version {Assembly.GetEntryAssembly()!
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion}");
        app.HelpOption("-?|-h|--help");

        CommandArgument srcArg = app.Argument("src", "DLL source path");
        CommandArgument outArg = app.Argument("out", "Output directory");

        CommandOption indexPageNameOption = app.Option(
            "--index-page-name",
            "Name of the index page, (default: \"index\").",
            CommandOptionType.SingleValue);

        CommandOption examplesPathOption = app.Option(
            "--examples-path",
            "Path to the code examples to insert in the documentation.",
            CommandOptionType.SingleValue);

        CommandOption gitHubPagesOption = app.Option(
            "--github-pages",
            "Remove '.md' extension from links for GitHub Pages.",
            CommandOptionType.NoValue);

        CommandOption gitlabWikiOption = app.Option(
            "--gitlab-wiki",
            "Remove '.md' extension and './' prefix from links for gitlab wikis.",
            CommandOptionType.NoValue);

        CommandOption backButtonOption = app.Option(
            "--back-button",
            "Add a back button on each page with custom text, (default:  \"< Back\").",
            CommandOptionType.SingleValue);

        CommandOption linkbackButtonOption = app.Option(
           "--link-back-button",
           "Set link for back button, (default:  \"./\").",
           CommandOptionType.SingleValue);

        CommandOption IncludePrivateMethodOption = app.Option(
            "--private-members",
            "Write documentation for private members.",
            CommandOptionType.NoValue);

        CommandOption OnlyInternalMethodOption = app.Option(
            "--onlyinternal-members",
            "Write documentation for only internal members.",
            CommandOptionType.NoValue);

        CommandOption ExcludeINternalsOption = app.Option(
            "--excludeinternal",
            "Exclude documentation for internal types.",
            CommandOptionType.NoValue);

        CommandOption TemplateOption = app.Option(
            "--templatefile",
            "Layout template for documentation, (default:  \"template.md\").",
            CommandOptionType.SingleValue);

        CommandOption backIndexButtonOption = app.Option(
            "--back-index-button",
            "Add a back button in index page, (default:  \"< Back\").",
            CommandOptionType.SingleValue);

        CommandOption linkbackIndexButtonOption = app.Option(
            "--link-backindex-button",
            "Set link for back button in index page, (default:  \"./\").",
            CommandOptionType.SingleValue);

        CommandOption externalDocsOption = app.Option(
            "--external-docs",
            "Add an external documentation mapping in the format <namespace>=<url-base> (repeatable).",
            CommandOptionType.MultipleValue);

        CommandOption externalDocsFileOption = app.Option(
            "--external-docs-file",
            "Path to a JSON file with namespace → URL-base mappings for external documentation.",
            CommandOptionType.SingleValue);

        CommandOption noLinkGenericArgsOption = app.Option(
            "--no-link-generic-arguments",
            "Disable individual links for each generic type argument (legacy rendering).",
            CommandOptionType.NoValue);

        CommandOption frontMatterOption = app.Option(
            "--front-matter",
            "YAML front matter (or @filepath) prepended to every generated type page. " +
            "Supports placeholders: {TypeName}, {Namespace}, {AssemblyName}, {Date}.",
            CommandOptionType.SingleValue);

        CommandOption indexFrontMatterOption = app.Option(
            "--index-front-matter",
            "YAML front matter (or @filepath) prepended to the generated index page. " +
            "Supports placeholders: {AssemblyName}, {Date}.",
            CommandOptionType.SingleValue);

        CommandOption failOnUnresolvedOption = app.Option(
            "--fail-on-unresolved",
            "Exit with a non-zero code if any type reference could not be resolved.",
            CommandOptionType.NoValue);

        app.OnExecute(() =>
        {
            string src = srcArg.Value;
            string @out = outArg.Value;
            string indexPageName = indexPageNameOption.Value() ?? "index";
            string templatePageName = TemplateOption.Value() ?? "template.md";
            string templatefile = null;
            string resultindexdoc = null;
            string backButtonOptiontext = backButtonOption.Value() ?? "< Back";
            string linkbackButtontext = linkbackButtonOption.Value() ?? "./";
            string backIndexButtonOptiontext = backIndexButtonOption.Value() ?? "< Back";
            string linkbackIndexButtontext = linkbackIndexButtonOption.Value() ?? "./";
            bool hastemplate = File.Exists(templatePageName);
            if (hastemplate)
            {
                templatefile = File.ReadAllText(templatePageName);
                if (!templatefile.Contains("{xmldoc2md-Body()}", StringComparison.InvariantCultureIgnoreCase))
                {
                    throw new ArgumentException("Invalid Template, not found token {xmldoc2md-Body()}");
                }
                Logger.Info($"Loaded template from : {templatePageName}");
                templatefile = File.ReadAllText(templatePageName);
                resultindexdoc = templatefile;
                if (resultindexdoc.Contains("{xmldoc2md-Back()}", StringComparison.InvariantCultureIgnoreCase))
                {
                    resultindexdoc = resultindexdoc.Replace("{xmldoc2md-Back()}", string.Empty, StringComparison.InvariantCultureIgnoreCase);
                }
                if (resultindexdoc.Contains("{xmldoc2md-BackIndex()}", StringComparison.InvariantCultureIgnoreCase))
                {
                    resultindexdoc = resultindexdoc.Replace("{xmldoc2md-BackIndex()}", $"[**{backIndexButtonOptiontext}**]({linkbackIndexButtontext})", StringComparison.InvariantCultureIgnoreCase);
                }
                if (resultindexdoc.Contains("{xmldoc2md-Title()}", StringComparison.InvariantCultureIgnoreCase))
                {
                    string titindex = indexPageName[0].ToString().ToUpper() + indexPageName[1..];
                    resultindexdoc = resultindexdoc.Replace("{xmldoc2md-Title()}", titindex, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            // Build external docs resolver
            XMLDoc2Markdown.Utils.ExternalDocsResolver externalDocsResolver = new();
            foreach (string mapping in externalDocsOption.Values)
            {
                int eq = mapping.IndexOf('=');
                if (eq > 0)
                {
                    externalDocsResolver.AddMapping(mapping[..eq], mapping[(eq + 1)..]);
                }
                else
                {
                    Logger.Warning($"Ignoring malformed --external-docs entry (expected namespace=url): {mapping}");
                }
            }

            if (!string.IsNullOrEmpty(externalDocsFileOption.Value()))
            {
                externalDocsResolver.LoadFromFile(externalDocsFileOption.Value());
            }

            string frontMatterTemplate = ResolveFrontMatterArg(frontMatterOption.Value());
            string indexFrontMatterTemplate = ResolveFrontMatterArg(indexFrontMatterOption.Value());

            TypeDocumentationOptions options = new()
            {
                ExamplesDirectory = examplesPathOption.Value(),
                GitHubPages = gitHubPagesOption.HasValue(),
                GitlabWiki = gitlabWikiOption.HasValue(),
                HasBackButton = !string.IsNullOrEmpty(backButtonOption.Value()),
                BackButton = backButtonOptiontext,
                LinkBackButton = linkbackButtontext,
                IncludePrivateMembers = IncludePrivateMethodOption.HasValue(),
                ExcludeInternals = ExcludeINternalsOption.HasValue(),
                OnlyInternalMembers = OnlyInternalMethodOption.HasValue(),
                FoundBackButtonTemplate = false,
                LinkGenericArguments = !noLinkGenericArgsOption.HasValue(),
                FrontMatter = frontMatterTemplate,
                IndexFrontMatter = indexFrontMatterTemplate,
                FailOnUnresolved = failOnUnresolvedOption.HasValue(),
                ExternalDocsResolver = externalDocsResolver
            };
            int succeeded = 0;
            int failed = 0;

            if (!Directory.Exists(@out))
            {
                Directory.CreateDirectory(@out);
            }
            
            AssemblyResolver resolver = new();
            string srcDir = Path.GetDirectoryName(Path.GetFullPath(src)) ?? Environment.CurrentDirectory;
            resolver.AddSearchDirectory(srcDir);

            Assembly assembly = new AssemblyLoadContext(src)
                .LoadFromAssemblyName(new AssemblyName(Path.GetFileNameWithoutExtension(src)));

            string assemblyName = assembly.GetName().Name;
            XmlDocumentation documentation = new(src);
            XmlDocumentationContext docContext = new(documentation);
            docContext.AddSearchDirectory(srcDir);
            Logger.Info($"Generation started: Assembly: {assemblyName}");

            IMarkdownDocument indexPage = new MarkdownDocument();

            indexPage.AppendHeader($"Assembly {assemblyName}", 1);

            if (!hastemplate && !string.IsNullOrEmpty(backIndexButtonOption.Value()))
            {
                indexPage.AppendParagraph(new MarkdownLink(new MarkdownInlineCode(backIndexButtonOptiontext), linkbackIndexButtontext));
            }
            
            IEnumerable<Type> types = options.ExcludeInternals
                ? assembly.GetLoadableTypes().Where(type => type.IsPublic && type.IsVisible)
                : assembly.GetLoadableTypes().Where(type => type.IsPublic);
            IEnumerable<IGrouping<string, Type>> typesByNamespace = types.GroupBy(type => type.Namespace).OrderBy(g => g.Key);
            foreach (IGrouping<string, Type> namespaceTypes in typesByNamespace)
            {
                indexPage.AppendHeader($"Namespace {namespaceTypes.Key ?? "No namespace"}", 2);

                foreach (Type type in namespaceTypes.OrderBy(x => x.Name))
                {
                    // exclude delegates
                    if (typeof(Delegate).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    string typename = type.IsGenericType ? type.PrettyTypeName() : type.Name;
                    string fileName = type.GetDocsFileName();
                    Logger.Info($"  {fileName}.md");

                    indexPage.AppendParagraph("- " + type.GetDocsLink(assembly, noExtension: options.GitHubPages));

                    try
                    {
                        string resultdoc;
                        if (hastemplate)
                        {
                            resultdoc = templatefile;
                            if (resultdoc.Contains("{xmldoc2md-Title()}", StringComparison.InvariantCultureIgnoreCase))
                            {
                                resultdoc = resultdoc
                                    .Replace("{xmldoc2md-Title()}",
                                        typename[0].ToString().ToUpper() + typename[1..],
                                        StringComparison.InvariantCultureIgnoreCase);
                            }

                            if (!string.IsNullOrEmpty(resultdoc) && resultdoc.Contains("{xmldoc2md-Back()}", StringComparison.InvariantCultureIgnoreCase))
                            {
                                options.FoundBackButtonTemplate = true;
                                resultdoc = resultdoc.Replace("{xmldoc2md-Back()}", $"[**{backButtonOptiontext}**]({linkbackButtontext})", StringComparison.InvariantCultureIgnoreCase);
                            }

                            if (resultdoc.Contains("{xmldoc2md-BackIndex()}", StringComparison.InvariantCultureIgnoreCase))
                            {
                                resultdoc = resultdoc.Replace("{xmldoc2md-BackIndex()}", string.Empty, StringComparison.InvariantCultureIgnoreCase);
                            }

                            resultdoc = resultdoc.Replace("{xmldoc2md-Body()}",
                                    new TypeDocumentation(assembly, type, docContext, options).ToString(),
                                    StringComparison.InvariantCultureIgnoreCase);
                        }
                        else
                        {
                            resultdoc = new TypeDocumentation(assembly, type, docContext, options).ToString();
                        }

                        // Prepend per-type front matter
                        if (!string.IsNullOrEmpty(options.FrontMatter))
                        {
                            string fm = ApplyFrontMatterPlaceholders(options.FrontMatter, typename, type.Namespace, assemblyName);
                            resultdoc = fm + resultdoc;
                        }

                        File.WriteAllText(
                            Path.Combine(@out, $"{fileName}.md"), resultdoc);
                        succeeded++;
                    }
                    catch (Exception exception)
                    {
                        Logger.Error(exception.Message);
                        failed++;
                    }
                }
            }

            if (hastemplate)
            {
                resultindexdoc = resultindexdoc.Replace("{xmldoc2md-Body()}",
                    indexPage.ToString(),
                    StringComparison.InvariantCultureIgnoreCase);
            }
            else
            {
                if (!hastemplate && !string.IsNullOrEmpty(backIndexButtonOption.Value()))
                {
                    indexPage.AppendHorizontalRule();
                    indexPage.AppendParagraph(new MarkdownLink(new MarkdownInlineCode(backIndexButtonOptiontext), linkbackIndexButtontext));
                }

                resultindexdoc = indexPage.ToString();
            }

            // Prepend index front matter
            if (!string.IsNullOrEmpty(options.IndexFrontMatter))
            {
                string fm = ApplyFrontMatterPlaceholders(options.IndexFrontMatter, null, null, assemblyName);
                resultindexdoc = fm + resultindexdoc;
            }

            File.WriteAllText(Path.Combine(@out, $"{indexPageName}.md"), resultindexdoc);

            Logger.Info($"Generation: {succeeded} succeeded, {failed} failed");

            if (options.FailOnUnresolved && (failed > 0 || options.UnresolvedCount > 0))
            {
                Logger.Error($"Exiting with error: {failed} generation failure(s), {options.UnresolvedCount} unresolved type reference(s).");
                return 1;
            }

            return 0;
        });

        try
        {
            return app.Execute(args);
        }
        catch (CommandParsingException ex)
        {
            Logger.Error(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Unable to generate documentation:");
            Logger.Error(ex.Message);
        }

        return 1;
    }

    /// <summary>
    /// Reads front matter value from the CLI argument: if it starts with <c>@</c> treats the
    /// remainder as a file path and reads the file; otherwise returns the string as-is.
    /// </summary>
    private static string ResolveFrontMatterArg(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.StartsWith('@'))
        {
            string path = value[1..];
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Front matter file not found: {path}", path);
            }

            return File.ReadAllText(path);
        }

        return value;
    }

    /// <summary>Replaces well-known placeholders in a front matter template string.</summary>
    private static string ApplyFrontMatterPlaceholders(string template, string typeName, string namespaceName, string assemblyName)
    {
        return template
            .Replace("{TypeName}", typeName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{Namespace}", namespaceName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{AssemblyName}", assemblyName ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{Date}", DateTime.UtcNow.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
    }
}
