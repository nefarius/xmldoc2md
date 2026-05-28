using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Markdown;

namespace XMLDoc2Markdown.Utils;

internal static class TypeExtensions
{
    internal static readonly IReadOnlyDictionary<Type, string> simplifiedTypeNames = new Dictionary<Type, string>
        {
            // void
            { typeof(void), "void" },
            // object
            { typeof(object), "object" },
            // boolean
            { typeof(bool), "bool" },
            // numeric
            { typeof(sbyte), "sbyte" },
            { typeof(byte), "byte" },
            { typeof(short), "short" },
            { typeof(ushort), "ushort" },
            { typeof(int), "int" },
            { typeof(uint), "uint" },
            { typeof(long), "long" },
            { typeof(ulong), "ulong" },
            { typeof(float), "float" },
            { typeof(double), "double" },
            { typeof(decimal), "decimal" },
            // text
            { typeof(char), "char" },
            { typeof(string), "string" },
        };

    internal static string GetSimplifiedName(this Type type)
    {
        return simplifiedTypeNames.TryGetValue(type, out string simplifiedName) ? simplifiedName : PrettyTypeName(type);
    }

    internal static Visibility GetVisibility(this Type type)
    {
        if (!type.IsPublic)
        {
            return Visibility.Private;
        }
        if (type.IsPublic && type.IsVisible)
        {
            return Visibility.Public;
        }
        if (type.IsPublic && !type.IsVisible)
        {
            return Visibility.Internal;
        }
        else
        {
            return Visibility.None;
        }
    }

    internal static string PrettyTypeName(this Type t)
    {
        if (t.IsArray)
        {
            return PrettyTypeName(t.GetElementType()) + "[]";
        }

        if (t.IsGenericType)
        {
            return string.Format(
                "{0}<{1}>",
                t.Name[..t.Name.LastIndexOf("`", StringComparison.InvariantCulture)],
                string.Join(", ", t.GetGenericArguments().Select(x => x.PrettyTypeName())));
        }

        return t.Name;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="type"/> is a C# record class or record struct.
    /// Record classes are identified by the compiler-generated <c>&lt;Clone&gt;$</c> method.
    /// Record structs are identified by the compiler-generated
    /// <c>PrintMembers(System.Text.StringBuilder)</c> method on a value type.
    /// </summary>
    internal static bool IsRecord(this Type type)
    {
        if (type == null)
        {
            return false;
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // Record classes have a compiler-generated <Clone>$ method
        if (type.IsClass)
        {
            return type.GetMethod("<Clone>$", all) != null;
        }

        // Record structs have a compiler-generated PrintMembers(System.Text.StringBuilder) method
        if (type.IsValueType && !type.IsEnum)
        {
            return type.GetMethod(
                "PrintMembers",
                all,
                null,
                [typeof(System.Text.StringBuilder)],
                null) != null;
        }

        return false;
    }

    /// <summary>
    /// Returns a "clean" fully-qualified type name that never contains assembly-qualified generic arguments.
    /// For example <c>System.IEquatable`1[[MyNS.Foo, ...]]</c> becomes <c>System.IEquatable&lt;Foo&gt;</c>.
    /// </summary>
    internal static string GetCleanFullName(this Type type)
    {
        if (type == null)
        {
            return string.Empty;
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        string baseName = type.GetGenericTypeDefinition().FullName;
        if (baseName == null)
        {
            return PrettyTypeName(type);
        }

        int backtick = baseName.IndexOf('`');
        if (backtick > 0)
        {
            baseName = baseName[..backtick];
        }

        string args = string.Join(", ", type.GetGenericArguments().Select(GetCleanFullName));
        return $"{baseName}<{args}>";
    }

    internal static string GetSignature(this Type type, bool full = false)
    {
        List<string> signature = new();

        if (full)
        {
            signature.Add(type.GetVisibility().Print());

            bool isRecord = type.IsRecord();

            if (isRecord && type.IsValueType)
            {
                signature.Add("record");
                signature.Add("struct");
            }
            else if (isRecord)
            {
                // sealed is implicit for records unless explicitly inherited; omit "sealed"
                if (type.IsAbstract)
                {
                    signature.Add("abstract");
                }

                signature.Add("record");
            }
            else if (type.IsClass)
            {
                if (type.IsAbstract && type.IsSealed)
                {
                    signature.Add("static");
                }
                else if (type.IsAbstract)
                {
                    signature.Add("abstract");
                }
                else if (type.IsSealed)
                {
                    signature.Add("sealed");
                }

                signature.Add("class");
            }
            else if (type.IsInterface)
            {
                signature.Add("interface");
            }
            else if (type.IsEnum)
            {
                signature.Add("enum");
            }
            else if (type.IsValueType)
            {
                signature.Add("struct");
            }
        }

        signature.Add(type.GetDisplayName());

        if (type.IsClass || type.IsInterface)
        {
            bool isRecord = type.IsRecord();
            List<Type> baseTypeAndInterfaces = new();

            if (type.IsClass && type.BaseType != null && type.BaseType != typeof(object))
            {
                Type baseType = type.BaseType;
                // Only suppress System.Object (already guarded above); real record bases are shown.
                bool isObjectBase = isRecord && baseType.FullName == "System.Object";
                if (!isObjectBase)
                {
                    baseTypeAndInterfaces.Add(baseType);
                }
            }

            IEnumerable<Type> interfaces = type.GetInterfaces();
            if (isRecord)
            {
                // Strip IEquatable<Self> and IComparable<Self> that records add automatically
                interfaces = interfaces.Where(i =>
                    !(i.IsGenericType &&
                      (i.GetGenericTypeDefinition() == typeof(IEquatable<>) ||
                       i.GetGenericTypeDefinition() == typeof(IComparable<>)) &&
                      i.GetGenericArguments().Length == 1 &&
                      i.GetGenericArguments()[0] == type));
            }

            baseTypeAndInterfaces.AddRange(interfaces);

            if (baseTypeAndInterfaces.Count > 0)
            {
                // Use clean rendering: never emit assembly-qualified FullName for generic args
                signature.Add($": {string.Join(", ", baseTypeAndInterfaces.Select(t => t.Namespace != type.Namespace ? t.GetCleanFullName() : PrettyTypeName(t)))}");
            }
        }

        return string.Join(' ', signature);
    }

    internal static string GetDisplayName(this Type type, bool simplifyName = false)
    {
        string name = simplifyName ? type.GetSimplifiedName() : PrettyTypeName(type);

        TypeInfo typeInfo = type.GetTypeInfo();
        Type[] genericParams = typeInfo.GenericTypeArguments.Length > 0 ? typeInfo.GenericTypeArguments : typeInfo.GenericTypeParameters;

        if (genericParams.Length > 0 && name.IndexOf('`') > 0)
        {
            name = name[..name.IndexOf('`')];
            name += $"<{string.Join(", ", genericParams.Select(t => t.GetDisplayName(simplifyName)))}>";
        }

        return name;
    }

    internal static IEnumerable<Type> GetInheritanceHierarchy(this Type type)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            yield return current;
        }
    }

    internal static string GetMSDocsUrl(this Type type, string msdocsBaseUrl = "https://learn.microsoft.com/dotnet/api")
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }
        if (type.Assembly != typeof(string).Assembly)
        {
            throw new InvalidOperationException($"{type.FullName} is not a mscorlib type.");
        }

        return $"{msdocsBaseUrl}/{type.GetDocsFileName()}";
    }

    internal static string GetInternalDocsUrl(this Type type, bool noExtension = false, bool noPrefix = false)
    {
        if (type == null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        string url = $"{type.GetDocsFileName()}";

        if (!noExtension)
        {
            url += ".md";
        }

        if (!noPrefix)
        {
            url = url.Insert(0, "./");
        }

        return url;
    }

    internal static string GetDocsFileName(this Type type)
    {
        RequiredArgument.NotNull(type, nameof(type));
        return type.GetIdentifier().ToLower().Replace('`', '-');
    }

    internal static MarkdownInlineElement GetDocsLink(
        this Type type,
        Assembly assembly,
        string text = null,
        bool noExtension = false,
        bool noPrefix = false,
        bool linkGenericArguments = false,
        ExternalDocsResolver externalDocsResolver = null)
    {
        // When rendering generic types with linked arguments, produce a composite element:
        // "List<[MyType](./mytype.md)>" instead of a single link to List`1.
        if (linkGenericArguments && type.IsGenericType && string.IsNullOrEmpty(text))
        {
            return BuildGenericLink(type, assembly, noExtension, noPrefix, externalDocsResolver);
        }

        if (string.IsNullOrEmpty(text))
        {
            text = type.GetDisplayName().FormatChevrons();
        }

        if (!string.IsNullOrEmpty(type.FullName)) // Generic type does not have full name
        {
            // User-configured external docs take precedence over the built-in MSDocsUrl fallback
            // so that callers can redirect BCL links to a mirror or intranet copy.
            if (externalDocsResolver != null && type.Assembly != assembly)
            {
                string externalUrl = externalDocsResolver.TryGetUrl(type);
                if (externalUrl != null)
                {
                    return new MarkdownLink(text, externalUrl);
                }
            }

            if (type.Assembly == typeof(string).Assembly)
            {
                return new MarkdownLink(text, type.GetMSDocsUrl());
            }
            else if (type.Assembly == assembly)
            {
                return new MarkdownLink(text, type.GetInternalDocsUrl(noExtension, noPrefix));
            }
        }

        return new MarkdownText(text);
    }

    /// <summary>
    /// Builds a composite Markdown inline element for a generic type where each type argument
    /// is also rendered as a link when possible.
    /// Example: <c>Wrapper&lt;[MyType](./mytype.md)&gt;</c>
    /// </summary>
    private static MarkdownInlineElement BuildGenericLink(
        Type type,
        Assembly assembly,
        bool noExtension,
        bool noPrefix,
        ExternalDocsResolver externalDocsResolver)
    {
        Type openGeneric = type.GetGenericTypeDefinition();
        string baseName = openGeneric.Name;
        int backtick = baseName.IndexOf('`');
        if (backtick > 0)
        {
            baseName = baseName[..backtick];
        }

        // Link the open generic itself
        MarkdownInlineElement outerLink = ResolveGenericBaseLink(
            openGeneric, baseName, assembly, noExtension, noPrefix, externalDocsResolver);


        // Recursively render each generic argument
        Type[] args = type.GetGenericArguments();
        string argText = string.Join(", ", args.Select(a =>
            a.GetDocsLink(assembly, null, noExtension, noPrefix, linkGenericArguments: true, externalDocsResolver)
             .ToString()));

        return new MarkdownText($"{outerLink}<{argText}>");
    }

    private static MarkdownInlineElement ResolveGenericBaseLink(
        Type openGeneric,
        string baseName,
        Assembly assembly,
        bool noExtension,
        bool noPrefix,
        ExternalDocsResolver externalDocsResolver)
    {
        if (!string.IsNullOrEmpty(openGeneric.FullName))
        {
            // User-configured resolver takes precedence over the built-in MSDocsUrl fallback.
            if (externalDocsResolver != null && openGeneric.Assembly != assembly)
            {
                string url = externalDocsResolver.TryGetUrl(openGeneric);
                if (url != null)
                {
                    return new MarkdownLink(baseName, url);
                }
            }

            if (openGeneric.Assembly == typeof(string).Assembly)
            {
                return new MarkdownLink(baseName, openGeneric.GetMSDocsUrl());
            }

            if (openGeneric.Assembly == assembly)
            {
                return new MarkdownLink(baseName, openGeneric.GetInternalDocsUrl(noExtension, noPrefix));
            }
        }

        return new MarkdownText(baseName);
    }
}

