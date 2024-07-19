using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Markdown;
using XMLDoc2Markdown.Utils;

namespace XMLDoc2Markdown;

internal partial class TypeDocumentation
{
    private const string BackingFieldName = ">k__BackingField";

    private readonly Assembly _assembly;
    private readonly IMarkdownDocument _document = new MarkdownDocument();
    private readonly XmlDocumentation _documentation;
    private readonly TypeDocumentationOptions _options;
    private readonly Type _type;

    public TypeDocumentation(Assembly assembly, Type type, XmlDocumentation documentation,
        TypeDocumentationOptions options)
    {
        RequiredArgument.NotNull(assembly, nameof(assembly));
        RequiredArgument.NotNull(type, nameof(type));
        RequiredArgument.NotNull(documentation, nameof(documentation));

        this._assembly = assembly;
        this._type = type;
        this._documentation = documentation;
        this._options = options ?? new TypeDocumentationOptions();
    }

    public override string ToString()
    {
        if (this._options.HasBackButton && !this._options.FoundBackButtonTemplate)
        {
            this.WriteBackButton(true);
        }

        this._document.AppendHeader(this._type.GetDisplayName().FormatChevrons(), 1);

        this._document.AppendParagraph($"Namespace: {this._type.Namespace}");

        XElement typeDocElement = this._documentation.GetMember(this._type);

        if (typeDocElement != null)
        {
            Logger.Info("    (documented)");
        }

        this.WriteObsolete();
        this.WriteMemberInfoSummary(typeDocElement);
        this.WriteMemberInfoSignature(this._type);
        this.WriteTypeParameters(this._type, typeDocElement);
        this.WriteInheritanceAndImplements();
        this.WriteMemberInfoRemarks(typeDocElement);

        if (this._type.IsEnum)
        {
            this.WriteEnumFields(this.GetFields().Where(m => !m.IsSpecialName),
                Attribute.IsDefined(this._type, typeof(FlagsAttribute)));
        }
        else
        {
            this.WriteMembersDocumentation(this.GetFields().OrderBy(x => x.Name));
        }

        this.WriteMembersDocumentation(this.GetProperties().OrderBy(x => x.Name));
        this.WriteMembersDocumentation(this.GetConstructors().OrderBy(x => x.Name));
        this.WriteMembersDocumentation(
            this._type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName)
                .Where(m => !m.IsPrivate || this._options.IncludePrivateMembers)
                .Where(m => !m.IsAssembly || (m.IsAssembly && !this._options.ExcludeInternals))
                .OrderBy(x => x.Name)
        );
        this.WriteMembersDocumentation(this.GetEvents().OrderBy(x => x.Name));

        bool example = this.WriteExample(this._type);
        if (example)
        {
            Logger.Info("    (example)");
        }

        if (this._options.HasBackButton && !this._options.FoundBackButtonTemplate)
        {
            this.WriteBackButton(bottom: true);
        }

        return this._document.ToString();
    }

    private void WriteBackButton(bool top = false, bool bottom = false)
    {
        if (top && bottom)
        {
            throw new ArgumentException("Back button cannot not be set to 'top' and 'bottom' at the same time.");
        }

        if (bottom)
        {
            this._document.AppendHorizontalRule();
        }

        this._document.AppendParagraph(new MarkdownLink(new MarkdownInlineCode(this._options.BackButton),
            this._options.LinkBackButton));

        if (top)
        {
            this._document.AppendHorizontalRule();
        }
    }

    private void WriteInheritanceAndImplements()
    {
        List<string> lines = new();

        if (this._type.BaseType != null)
        {
            IEnumerable<MarkdownInlineElement> inheritanceHierarchy = this._type.GetInheritanceHierarchy()
                .Reverse()
                .Select(t => t.GetDocsLink(
                    this._assembly,
                    noExtension: this._options.GitHubPages || this._options.GitlabWiki,
                    noPrefix: this._options.GitlabWiki));
            lines.Add($"Inheritance {string.Join(" → ", inheritanceHierarchy)}");
        }

        Type[] interfaces = this._type.GetInterfaces();
        if (interfaces.Length > 0)
        {
            IEnumerable<MarkdownInlineElement> implements = interfaces
                .Select(i => i.GetDocsLink(
                    this._assembly,
                    noExtension: this._options.GitHubPages || this._options.GitlabWiki,
                    noPrefix: this._options.GitlabWiki));
            lines.Add($"Implements {string.Join(", ", implements)}");
        }

        if (lines.Any())
        {
            this._document.AppendParagraph(string.Join($"<br>{Environment.NewLine}", lines));
        }
    }

    private void WriteObsolete()
    {
        IEnumerable<ObsoleteAttribute> attribute = this._type.GetCustomAttributes<ObsoleteAttribute>();
        WriteObsolete(attribute, this._document, "This type is obsolete.");
    }

    private void WriteObsoleteMember(MemberInfo member)
    {
        IEnumerable<ObsoleteAttribute> attribute = member.GetCustomAttributes<ObsoleteAttribute>();
        WriteObsolete(attribute, this._document, "This member is obsolete.");
    }

    private static void WriteObsolete(IEnumerable<ObsoleteAttribute> attribute, IMarkdownDocument document,
        string defaultMessage)
    {
        if (attribute.Any())
        {
            document.AppendHeader("Caution", 4);

            string message = attribute.First().Message;
            if (string.IsNullOrEmpty(message))
            {
                document.AppendParagraph(defaultMessage);
            }
            else
            {
                document.AppendParagraph(message);
            }

            document.AppendHorizontalRule();
        }
    }

    private void WriteMemberInfoSummary(XElement memberDocElement)
    {
        IEnumerable<XNode> nodes = memberDocElement?.Element("summary")?.Nodes();
        if (nodes != null)
        {
            MarkdownParagraph summary = this.XNodesToMarkdownParagraph(nodes);
            this._document.Append(summary);
        }
    }

    private void WriteMemberInfoRemarks(XElement memberDocElement)
    {
        IEnumerable<XNode> nodes = memberDocElement?.Element("remarks")?.Nodes();
        if (nodes != null)
        {
            this._document.AppendParagraph(new MarkdownStrongEmphasis("Remarks:"));
            this._document.Append(this.XNodesToMarkdownParagraph(nodes));
        }
    }

    private object XElementToMarkdown(XElement element)
    {
        return element.Name.ToString() switch
        {
            "see" => this.GetLinkFromReference(element.Attribute("cref")?.Value ?? element.Attribute("href")?.Value,
                element.Value),
            "seealso" => this.GetLinkFromReference(element.Attribute("cref")?.Value, element.Value),
            "c" => new MarkdownInlineCode(element.Value),
            "br" => new MarkdownText($"<br>{element.Value ?? string.Empty}"),
            "para" => this.XNodesToMarkdownParagraph(element.Nodes()),
            "example" => this.XNodesToMarkdownParagraph(element.Nodes()),
            "code" => new MarkdownCode("csharp", FormatCodeElementValue(element.Value)),
            "list" => this.XElementToMarkdownList(element),
            _ => new MarkdownText(element.Value)
        };
    }

    private static string FormatCodeElementValue(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return code;
        }

        code = code.TrimStart('\n');
        code = code.TrimEnd('\n', ' ');

        static int getIndent(string value)
        {
            int indent = 0;
            foreach (char @char in value)
            {
                if (@char == ' ')
                {
                    indent++;
                }
                else
                {
                    break;
                }
            }

            return indent;
        }

        static string reindentLine(string line, int indent)
        {
            string result = string.Empty;
            int i;
            for (i = 0; i < indent; i++)
            {
                if (line[i] != ' ')
                {
                    break;
                }
            }

            return line[i..];
        }

        int indent = getIndent(code);

        IEnumerable<string> lines = code
            .Split('\n')
            .Select(line => reindentLine(line, indent));

        return string.Join(Environment.NewLine, lines);
    }

    private MarkdownParagraph XNodesToMarkdownParagraph(IEnumerable<XNode> nodes)
    {
        List<IMarkdownBlockElement> blocks = new();
        MarkdownText paragraph = null;
        if (nodes is null)
        {
            return new MarkdownParagraph("");
        }

        foreach (XNode node in nodes)
        {
            object element = this.XNodeToMarkdown(node);
            if (element is null)
            {
                continue;
            }

            switch (element)
            {
                case MarkdownInlineElement inlineElement:
                    if (paragraph is null)
                    {
                        paragraph = new MarkdownText(inlineElement);
                    }
                    else
                    {
                        paragraph.Append(inlineElement);
                    }

                    break;

                case IMarkdownBlockElement block:
                    if (paragraph is not null)
                    {
                        blocks.Add(new MarkdownParagraph(paragraph));
                        paragraph = null;
                    }

                    blocks.Add(block);
                    break;
            }
        }

        if (paragraph is not null)
        {
            blocks.Add(new MarkdownParagraph(paragraph));
        }

        return new MarkdownParagraph(string.Join(Environment.NewLine, blocks));
    }

    private MarkdownList XElementToMarkdownList(XElement element)
    {
        MarkdownList markdownList = element.Attribute("type")?.Value switch
        {
            "number" => new MarkdownOrderedList(),
            _ => new MarkdownList()
        };

        foreach (XElement item in element.Elements("item"))
        {
            MarkdownText markdownListItem = new(string.Empty);

            IEnumerable<XNode> term = item.Element("term").Nodes();

            MarkdownText markdownTerm = null;

            foreach (XNode node in term)
            {
                object md = this.XNodeToMarkdown(node);
                if (md is MarkdownInlineElement inlineElement)
                {
                    if (markdownTerm is null)
                    {
                        markdownTerm = new MarkdownText(inlineElement);
                    }
                    else
                    {
                        markdownTerm.Append(inlineElement);
                    }
                }
            }

            if (markdownTerm is not null)
            {
                markdownListItem.Append(new MarkdownStrongEmphasis(markdownTerm));
            }

            IEnumerable<XNode> description = item.Element("description").Nodes();

            MarkdownText markdownDescription = null;

            foreach (XNode node in description)
            {
                object md = this.XNodeToMarkdown(node);
                if (md is MarkdownInlineElement inlineElement)
                {
                    if (markdownDescription is null)
                    {
                        markdownDescription = new MarkdownText(inlineElement);
                    }
                    else
                    {
                        markdownDescription.Append(inlineElement);
                    }
                }
            }

            if (markdownDescription is not null)
            {
                markdownListItem.Append(" - ");
                markdownListItem.Append(markdownDescription);
            }

            markdownList.AddItem(markdownListItem);
        }

        return markdownList;
    }

    private object XNodeToMarkdown(XNode node)
    {
        return node switch
        {
            XText text => new MarkdownText(NodeToTextRegex().Replace(text.ToString(), " ")),
            XElement element => this.XElementToMarkdown(element),
            _ => null
        };
    }

    private void WriteMemberInfoSignature(MemberInfo memberInfo)
    {
        this._document.AppendCode(
            "csharp",
            memberInfo.GetSignature(true));
    }

    private void WriteMembersDocumentation(IEnumerable<MemberInfo> members)
    {
        RequiredArgument.NotNull(members, nameof(members));

        members = members.Where(member => member != null);

        if (!members.Any())
        {
            return;
        }

        MemberTypes memberType = members.First().MemberType;
        string title = memberType switch
        {
            MemberTypes.Property => "Properties",
            MemberTypes.Constructor => "Constructors",
            MemberTypes.Method => "Methods",
            MemberTypes.Event => "Events",
            MemberTypes.Field => "Fields",
            _ => throw new NotImplementedException()
        };
        this._document.AppendHeader(title, 2);
        Logger.Info($"    {title}");

        foreach (MemberInfo member in members)
        {
            this._document.AppendHeader(
                $"<a id=\"{title.ToLowerInvariant()}-{member.Name.ToLowerInvariant()}\"/>" +
                new MarkdownStrongEmphasis(member.GetSignature().FormatChevrons()), 3);

            XElement memberDocElement = this._documentation.GetMember(member);

            this.WriteObsoleteMember(member);
            this.WriteMemberInfoSummary(memberDocElement);
            this.WriteMemberInfoSignature(member);

            if (member is MethodBase methodBase)
            {
                this.WriteTypeParameters(methodBase, memberDocElement);
                this.WriteMethodParams(methodBase, memberDocElement);

                if (methodBase is MethodInfo methodInfo && methodInfo.ReturnType != typeof(void))
                {
                    this.WriteMethodReturnType(methodInfo, memberDocElement);
                }
            }

            if (member is PropertyInfo propertyInfo)
            {
                this._document.AppendHeader("Property Value", 4);

                MarkdownInlineElement typeName = propertyInfo.GetReturnType()?
                    .GetDocsLink(
                        this._assembly,
                        noExtension: this._options.GitHubPages || this._options.GitlabWiki,
                        noPrefix: this._options.GitlabWiki);
                IEnumerable<XNode> nodes = memberDocElement?.Element("value")?.Nodes();
                MarkdownParagraph valueDoc = this.XNodesToMarkdownParagraph(nodes);

                this._document.AppendParagraph($"{typeName}<br>{Environment.NewLine}{valueDoc}");
            }

            this.WriteExceptions(memberDocElement);
            this.WriteMemberInfoRemarks(memberDocElement);

            bool example = this.WriteExample(member);

            string log = $"      {member.GetIdentifier()}";
            if (memberDocElement != null)
            {
                log += " (documented)";
            }

            if (example)
            {
                log += " (example)";
            }

            Logger.Info(log);
        }
    }

    private void WriteExceptions(XElement memberDocElement)
    {
        IEnumerable<XElement> exceptionDocs = memberDocElement?.Elements("exception");

        if (!(exceptionDocs?.Count() > 0))
        {
            return;
        }

        this._document.AppendHeader("Exceptions", 4);

        foreach (XElement exceptionDoc in exceptionDocs)
        {
            string cref = exceptionDoc.Attribute("cref")?.Value;
            MarkdownInlineElement exceptionTypeName = this.GetLinkFromReference(cref);
            MarkdownParagraph exceptionSummary = this.XNodesToMarkdownParagraph(exceptionDoc.Nodes());

            this._document.AppendParagraph(
                string.Join($"<br>{Environment.NewLine}", exceptionTypeName, exceptionSummary));
        }
    }

    private void WriteMethodReturnType(MethodInfo methodInfo, XElement memberDocElement)
    {
        RequiredArgument.NotNull(methodInfo, nameof(methodInfo));

        this._document.AppendHeader("Returns", 4);

        MarkdownInlineElement typeName = methodInfo.ReturnType.GetDocsLink(
            this._assembly,
            noExtension: this._options.GitHubPages || this._options.GitlabWiki,
            noPrefix: this._options.GitlabWiki);
        IEnumerable<XNode> nodes = memberDocElement?.Element("returns")?.Nodes();

        if (nodes != null && nodes.Any())
        {
            MarkdownParagraph typeParamDoc = this.XNodesToMarkdownParagraph(nodes);
            this._document.AppendParagraph($"{typeParamDoc}");
        }
        else
        {
            this._document.AppendParagraph($"{typeName}");
        }
    }

    private void WriteTypeParameters(MemberInfo memberInfo, XElement memberDocElement)
    {
        RequiredArgument.NotNull(memberInfo, nameof(memberInfo));

        Type[] typeParams = memberInfo switch
        {
            TypeInfo typeInfo => typeInfo.GenericTypeParameters,
            MethodInfo methodInfo => methodInfo.GetGenericArguments(),
            _ => Array.Empty<Type>()
        };

        if (typeParams.Length == 0)
        {
            return;
        }

        this._document.AppendHeader("Type Parameters", 4);

        foreach (Type typeParam in typeParams)
        {
            MarkdownInlineElement typeName = typeParam.GetDocsLink(
                this._assembly,
                noExtension: this._options.GitHubPages || this._options.GitlabWiki,
                noPrefix: this._options.GitlabWiki);
            IEnumerable<XNode> nodes = memberDocElement?.Elements("typeparam")
                .FirstOrDefault(e => e.Attribute("name")?.Value == typeParam.Name)?.Nodes();
            MarkdownParagraph typeParamDoc = this.XNodesToMarkdownParagraph(nodes);

            this._document.AppendParagraph(string.Join($"<br>{Environment.NewLine}", new MarkdownInlineCode(typeName),
                typeParamDoc));
        }
    }

    private void WriteMethodParams(MethodBase methodBase, XElement memberDocElement)
    {
        RequiredArgument.NotNull(methodBase, nameof(methodBase));

        ParameterInfo[] @params = methodBase.GetParameters();

        if (@params.Length == 0)
        {
            return;
        }

        this._document.AppendHeader("Parameters", 4);

        foreach (ParameterInfo param in @params)
        {
            MarkdownInlineElement typeName = param.ParameterType.GetDocsLink(
                this._assembly,
                noExtension: this._options.GitHubPages || this._options.GitlabWiki,
                noPrefix: this._options.GitlabWiki);
            IEnumerable<XNode> nodes = memberDocElement?.Elements("param")
                .FirstOrDefault(e => e.Attribute("name")?.Value == param.Name)?.Nodes();
            MarkdownParagraph paramDoc = this.XNodesToMarkdownParagraph(nodes);

            this._document.AppendParagraph(
                $"{new MarkdownInlineCode(param.Name)} {typeName}<br>{Environment.NewLine}{paramDoc}");
        }
    }

    private void WriteEnumFields(IEnumerable<FieldInfo> fields, bool isFlag)
    {
        RequiredArgument.NotNull(fields, nameof(fields));

        if (!fields.Any())
        {
            return;
        }

        if (isFlag)
        {
            this._document.AppendHeader("Fields (Flags)", 2);
        }
        else
        {
            this._document.AppendHeader("Fields", 2);
        }

        MarkdownTableHeader header = new(
            new MarkdownTableHeaderCell("Name"),
            new MarkdownTableHeaderCell("Value", MarkdownTableTextAlignment.Right),
            new MarkdownTableHeaderCell("Description")
        );

        MarkdownTable table = new(header, fields.Count());

        foreach (FieldInfo field in fields)
        {
            IEnumerable<XNode> nodes = this._documentation.GetMember(field)?.Element("summary")?.Nodes();
            if (nodes == null)
            {
                continue;
            }

            MarkdownParagraph summary = this.XNodesToMarkdownParagraph(nodes);
            string formattedSummary = TableFormat(summary.ToString());

            table.AddRow(new MarkdownTableRow(field.Name, ((Enum)Enum.Parse(this._type, field.Name)).ToString("D"),
                formattedSummary));
        }

        this._document.Append(table);
    }

    private static string TableFormat(string input)
    {
        input = input.Replace("\r\n", "\n");
        StringBuilder sb = new(input.Length);

        foreach (string line in input.Split("\n"))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.Append(line);
            }
        }

        return sb.ToString();
    }

    private bool WriteExample(MemberInfo memberInfo)
    {
        if (this._options.ExamplesDirectory == null)
        {
            return false;
        }

        string fileName = $"{memberInfo.GetIdentifier()}.md";
        string file = Path.Combine(this._options.ExamplesDirectory, fileName);

        if (File.Exists(file))
        {
            try
            {
                using StreamReader reader = new(file);
                this._document.Append(new MarkdownParagraph(reader.ReadToEnd()));

                return true;
            }
            catch (IOException e)
            {
                Logger.Warning(e.Message);
            }
        }

        return false;
    }

    private MarkdownInlineElement GetLinkFromReference(string crefAttribute, string text = null)
    {
        if (this.TryGetMemberInfoFromReference(crefAttribute, out MemberInfo memberInfo))
        {
            return memberInfo.GetDocsLink(
                this._assembly,
                text,
                this._options.GitHubPages || this._options.GitlabWiki,
                this._options.GitlabWiki);
        }

        return new MarkdownText(text ?? crefAttribute);
    }

    private bool TryGetMemberInfoFromReference(string crefAttribute, out MemberInfo memberInfo)
    {
        memberInfo = null;

        if (crefAttribute == null ||
            crefAttribute.Length <= 2 ||
            crefAttribute[1] != ':' ||
            !MemberTypesAliases.TryGetMemberType(crefAttribute[0], out MemberTypes memberType))
        {
            return memberInfo != null;
        }

        string memberFullName = crefAttribute[2..];

        if (memberType is MemberTypes.Constructor or MemberTypes.Method)
        {
            (string @namespace, string methodSignature, int genericCount, int parameterCount) =
                DeconstructMember(memberFullName);
            Type type = this.GetTypeFromFullName(@namespace);
            if (type is not null)
            {
                memberInfo = type.GetMember($"{methodSignature}*")
                                 .FirstOrDefault(info =>
                                 {
                                     MethodBase methodBase = (MethodBase)info;
                                     if (methodBase.ContainsGenericParameters
                                         && methodBase.GetGenericArguments().Length != genericCount)
                                     {
                                         return false;
                                     }

                                     return methodBase.GetParameters().Length == parameterCount;
                                 }) ??
                             type.GetMember($"{methodSignature}*").FirstOrDefault();
            }
        }
        else if (memberType is MemberTypes.Event or MemberTypes.Field or MemberTypes.Property)
        {
            int idx = memberFullName.LastIndexOf(".");
            Type type = this.GetTypeFromFullName(memberFullName[..idx]);
            if (type is not null)
            {
                memberInfo = type.GetMember(memberFullName[(idx + 1)..]).FirstOrDefault();
            }
        }
        else if (memberType is MemberTypes.TypeInfo or MemberTypes.NestedType)
        {
            Type type = this.GetTypeFromFullName(memberFullName);
            if (type is not null)
            {
                memberInfo = type;
            }
        }

        return memberInfo != null;
    }

    private static (string @namespace, string methodName, int genericCount, int parameterCount)
        DeconstructMember(string input)
    {
        int genericIndex = input.IndexOf("``");
        int parameterIndex = input.IndexOf("(");
        int genericCount = 0;
        int parameterCount = 0;

        string parameterStripped = parameterIndex > -1 ? input[..parameterIndex] : input;
        int lastDotIndex = parameterStripped.LastIndexOf('.');
        string @namespace = input[..lastDotIndex];

        string methodName = input[(lastDotIndex + 1)..];

        if (parameterIndex > -1)
        {
            parameterCount = input[parameterIndex..].Split(',').Length;
            methodName = input[(lastDotIndex + 1)..parameterIndex];
        }

        if (genericIndex > -1)
        {
            genericCount = parameterIndex > 1
                ? int.Parse(input[(genericIndex + 2)..parameterIndex])
                : int.Parse(input[(genericIndex + 2)..]);
            methodName = input[(lastDotIndex + 1)..genericIndex];
        }

        return (@namespace, methodName.Replace('#', '.'), genericCount, parameterCount);
    }

    private IEnumerable<FieldInfo> GetFields()
    {
        if (this._options.IncludePrivateMembers)
        {
            if (this._options.ExcludeInternals)
            {
                return this._type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static)
                    .Where(x => !x.Name.EndsWith(BackingFieldName) && !x.IsAssembly);
            }

            if (this._options.OnlyInternalMembers)
            {
                return this._type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static)
                    .Where(x => !x.Name.EndsWith(BackingFieldName) && x.IsAssembly);
            }

            return this._type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                       BindingFlags.Static)
                .Where(x => !x.Name.EndsWith(BackingFieldName));
        }

        return this._type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private IEnumerable<PropertyInfo> GetProperties()
    {
        if (this._options.IncludePrivateMembers)
        {
            if (this._options.ExcludeInternals)
            {
                return this._type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                               BindingFlags.Static)
                    .Where(x => x.GetVisibility() != Visibility.Internal &&
                                x.GetVisibility() != Visibility.ProtectedInternal);
            }

            if (this._options.OnlyInternalMembers)
            {
                return this._type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                               BindingFlags.Static)
                    .Where(x => x.GetVisibility() == Visibility.Internal ||
                                x.GetVisibility() == Visibility.ProtectedInternal);
            }

            return this._type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static);
        }

        return this._type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private IEnumerable<ConstructorInfo> GetConstructors()
    {
        if (this._options.IncludePrivateMembers)
        {
            if (this._options.ExcludeInternals)
            {
                return this._type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                 BindingFlags.Static)
                    .Where(x => !x.IsAssembly);
            }

            if (this._options.OnlyInternalMembers)
            {
                return this._type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                                 BindingFlags.Static)
                    .Where(x => x.IsAssembly);
            }

            return this._type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                             BindingFlags.Static);
        }

        return this._type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private IEnumerable<EventInfo> GetEvents()
    {
        if (this._options.IncludePrivateMembers)
        {
            if (this._options.ExcludeInternals)
            {
                return this._type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static)
                    .Where(x => x.GetVisibility() != Visibility.Internal &&
                                x.GetVisibility() != Visibility.ProtectedInternal);
            }

            if (this._options.OnlyInternalMembers)
            {
                return this._type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                           BindingFlags.Static)
                    .Where(x => x.GetVisibility() == Visibility.Internal ||
                                x.GetVisibility() == Visibility.ProtectedInternal);
            }

            return this._type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                       BindingFlags.Static);
        }

        return this._type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
    }

    private Type GetTypeFromFullName(string typeFullName)
    {
        return Type.GetType(typeFullName) ?? this._assembly.GetType(typeFullName);
    }

    [GeneratedRegex("[ ]{2,}")]
    private static partial Regex NodeToTextRegex();
}
