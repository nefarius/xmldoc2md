using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using XMLDoc2Markdown.Utils;

namespace XMLDoc2Markdown;

/// <summary>
/// Resolves <c>&lt;inheritdoc/&gt;</c> and <c>&lt;inheritdoc cref="..."/&gt;</c> elements by
/// walking the member's inheritance chain and searching all registered XML documentation sources.
/// </summary>
internal static class InheritDocResolver
{
    /// <summary>
    /// Returns the effective documentation element for <paramref name="memberInfo"/>, resolving
    /// any top-level <c>&lt;inheritdoc/&gt;</c> element encountered.  Returns <c>null</c> when
    /// no documentation can be found.
    /// </summary>
    public static XElement Resolve(MemberInfo memberInfo, XmlDocumentationContext context)
    {
        return ResolveCore(memberInfo, context, new HashSet<string>(StringComparer.Ordinal));
    }

    private static XElement ResolveCore(MemberInfo memberInfo, XmlDocumentationContext context, HashSet<string> visited)
    {
        string id = $"{memberInfo.MemberType.GetAlias()}:{memberInfo.GetIdentifier()}";

        if (!visited.Add(id))
        {
            // Cycle detected — stop recursion
            return null;
        }

        XElement element = context.GetMember(memberInfo);

        if (element == null)
        {
            // No XML at all — try inherited sources
            return TryInherit(memberInfo, context, visited, cref: null);
        }

        XElement inheritDocNode = element.Element("inheritdoc");
        if (inheritDocNode == null)
        {
            // Normal documented member — return as-is
            return element;
        }

        // Has <inheritdoc/> — look for optional cref
        string cref = inheritDocNode.Attribute("cref")?.Value;
        if (!string.IsNullOrWhiteSpace(cref))
        {
            return ResolveFromCref(cref, context, visited);
        }

        return TryInherit(memberInfo, context, visited, cref: null);
    }

    /// <summary>
    /// Resolves from an explicit <c>cref</c> attribute value.
    /// </summary>
    private static XElement ResolveFromCref(string cref, XmlDocumentationContext context, HashSet<string> visited)
    {
        if (!visited.Add("cref:" + cref))
        {
            return null;
        }

        // Try to find the element directly in the context
        if (context.TryGetMember(cref, out XElement found, out _))
        {
            // If the found element itself has inheritdoc, keep resolving
            XElement innerInheritDoc = found?.Element("inheritdoc");
            if (innerInheritDoc != null)
            {
                string innerCref = innerInheritDoc.Attribute("cref")?.Value;
                return innerCref != null
                    ? ResolveFromCref(innerCref, context, visited)
                    : found; // Can't walk further without a MemberInfo
            }

            return found;
        }

        return null;
    }

    /// <summary>
    /// Walks base types and declared interfaces to find documentation for the same member signature.
    /// </summary>
    private static XElement TryInherit(MemberInfo memberInfo, XmlDocumentationContext context, HashSet<string> visited, string cref)
    {
        IEnumerable<Type> candidates = GetInheritanceCandidates(memberInfo);

        foreach (Type candidate in candidates)
        {
            MemberInfo inherited = FindMatchingMember(memberInfo, candidate);
            if (inherited == null)
            {
                continue;
            }

            XElement result = ResolveCore(inherited, context, visited);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the types to search for inherited documentation: base type first, then interfaces,
    /// all in declaration order.
    /// </summary>
    private static IEnumerable<Type> GetInheritanceCandidates(MemberInfo memberInfo)
    {
        Type declaringType = memberInfo switch
        {
            Type t => t,
            _ => memberInfo.DeclaringType
        };

        if (declaringType == null)
        {
            yield break;
        }

        // For a type itself — walk its own base chain
        if (memberInfo is Type selfType)
        {
            if (selfType.BaseType != null && selfType.BaseType != typeof(object))
            {
                yield return selfType.BaseType;
            }

            foreach (Type iface in selfType.GetInterfaces())
            {
                yield return iface;
            }

            yield break;
        }

        // For a member — search the same member in base type then interfaces
        if (declaringType.BaseType != null && declaringType.BaseType != typeof(object))
        {
            yield return declaringType.BaseType;
        }

        foreach (Type iface in declaringType.GetInterfaces())
        {
            yield return iface;
        }
    }

    private static MemberInfo FindMatchingMember(MemberInfo original, Type searchIn)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        switch (original)
        {
            case MethodInfo method:
            {
                ParameterInfo[] parameters = method.GetParameters();
                return searchIn
                    .GetMethods(flags)
                    .FirstOrDefault(m =>
                        m.Name == method.Name &&
                        m.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters.Select(p => p.ParameterType)));
            }

            case PropertyInfo prop:
                return searchIn.GetProperty(prop.Name, flags);

            case EventInfo evt:
                return searchIn.GetEvent(evt.Name, flags);

            case FieldInfo field:
                return searchIn.GetField(field.Name, flags);

            default:
                return null;
        }
    }
}
