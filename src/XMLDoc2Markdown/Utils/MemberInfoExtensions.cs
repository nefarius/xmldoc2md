using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Markdown;

namespace XMLDoc2Markdown.Utils;

internal static class MemberInfoExtensions
{
    internal static string GetSignature(this MemberInfo memberInfo, bool full = false)
    {
        if (memberInfo is Type type)
        {
            return type.GetSignature(full);
        }
        else if (memberInfo is MethodBase methodBase)
        {
            return methodBase.GetSignature(full);
        }
        else if (memberInfo is PropertyInfo propertyInfo)
        {
            return propertyInfo.GetSignature(full);
        }
        else if (memberInfo is EventInfo eventInfo)
        {
            return eventInfo.GetSignature(full);
        }
        else if (memberInfo is FieldInfo fieldInfo)
        {
            return fieldInfo.GetSignature(full);
        }

        throw new NotImplementedException();
    }

    internal static string GetIdentifier(this MemberInfo memberInfo)
    {
        switch (memberInfo)
        {
            case Type type:
                return Regex.Replace(type.FullName ?? type.Name, @"\[.*\]", string.Empty).Replace('+', '.');

            case PropertyInfo _:
            case FieldInfo _:
            case EventInfo _:
                return memberInfo.DeclaringType.GetIdentifier() + "." + memberInfo.Name;

            case MethodBase methodBase:
                // A method on IEquatable<OpenResult<T>> is a constructed member. Documentation IDs
                // address the generic definition, where T is that definition's own type parameter.
                methodBase = AsDeclaredOnGenericDefinition(methodBase);
                Type contextType = methodBase.DeclaringType;
                ParameterInfo[] parameterInfos = methodBase.GetParameters();

                string identifier = GetFormatedTypeName(contextType, false, contextType);
                identifier += ".";
                identifier += methodBase is ConstructorInfo ? "#ctor" : methodBase.Name;
                if (methodBase.IsGenericMethod)
                {
                    identifier += "``" + methodBase.GetGenericArguments().Length;
                }

                identifier += parameterInfos.Length > 0
                    ? $@"({string.Join(
                        ",",
                        parameterInfos.Select(x => GetFormatedTypeName(x.ParameterType, true, contextType)))})"
                    : string.Empty;

                if (methodBase is MethodInfo methodInfo &&
                    (methodBase.Name == "op_Implicit" ||
                    methodBase.Name == "op_Explicit"))
                {
                    identifier += "~" + GetFormatedTypeName(methodInfo.ReturnType, true, contextType);
                }

                return identifier;

            default:
                throw new Exception($"{nameof(GetIdentifier)} encountered an unhandled member info: {memberInfo}");
        }
    }

    internal static string GetMSDocsUrl(this MemberInfo memberInfo, string msdocsBaseUrl = "https://learn.microsoft.com/dotnet/api")
    {
        RequiredArgument.NotNull(memberInfo, nameof(memberInfo));

        Type type = memberInfo.DeclaringType ?? throw new Exception($"Property {memberInfo.Name} has no declaring type.");

        if (type.Assembly != typeof(string).Assembly)
        {
            throw new InvalidOperationException($"{type.FullName} is not a mscorlib type.");
        }

        return $"{msdocsBaseUrl}/{type.GetDocsFileName()}.{memberInfo.Name.ToLower()}";
    }

    internal static string GetInternalDocsUrl(this MemberInfo memberInfo, bool noExtension = false, bool noPrefix = false)
    {
        RequiredArgument.NotNull(memberInfo, nameof(memberInfo));

        Type type = memberInfo.DeclaringType ?? throw new Exception($"Event {memberInfo.Name} has no declaring type.");

        string url = $"{type.GetDocsFileName()}";

        if (!noExtension)
        {
            url += ".md";
        }

        if (!noPrefix)
        {
            url = url.Insert(0, "./");
        }

        string anchor = memberInfo.Name.ToAnchorLink();

        return $"{url}#{anchor}";
    }

    internal static MarkdownInlineElement GetDocsLink(
        this MemberInfo memberInfo,
        Assembly assembly,
        string text = null,
        bool noExtension = false,
        bool noPrefix = false,
        bool linkGenericArguments = false,
        ExternalDocsResolver externalDocsResolver = null)
    {
        RequiredArgument.NotNull(memberInfo, nameof(memberInfo));
        RequiredArgument.NotNull(assembly, nameof(assembly));

        return memberInfo switch
        {
            Type type => type.GetDocsLink(assembly, text, noExtension, noPrefix, linkGenericArguments, externalDocsResolver),
            MethodBase method => method.GetDocsLink(assembly, text, noExtension, noPrefix),
            _ => getDocsLinkBase(memberInfo, assembly, text, noExtension, noPrefix, externalDocsResolver),
        };

        static MarkdownInlineElement getDocsLinkBase(
            MemberInfo memberInfo,
            Assembly assembly,
            string text = null,
            bool noExtension = false,
            bool noPrefix = false,
            ExternalDocsResolver externalDocsResolver = null)
        {
            Type declaringType = memberInfo.DeclaringType;

            if (declaringType is not null)
            {
                if (string.IsNullOrEmpty(text))
                {
                    text = $"{declaringType.GetDisplayName().FormatChevrons()}.{memberInfo.Name}";
                }

                if (declaringType.Assembly == typeof(string).Assembly)
                {
                    return new MarkdownLink(text, memberInfo.GetMSDocsUrl());
                }
                else if (declaringType.Assembly == assembly)
                {
                    return new MarkdownLink(text, memberInfo.GetInternalDocsUrl(noExtension, noPrefix));
                }

                if (externalDocsResolver != null)
                {
                    string url = externalDocsResolver.TryGetUrl(declaringType);
                    if (url != null)
                    {
                        return new MarkdownLink(text, url);
                    }
                }

                return new MarkdownText(text);
            }

            return new MarkdownText(text ?? memberInfo.Name);
        }
    }

    /// <summary>
    /// Returns <paramref name="method"/> as declared on its generic type definition.
    /// Constructed declaring types, such as <c>IEquatable&lt;OpenResult&lt;T&gt;&gt;</c>, otherwise
    /// expose substituted type arguments whose nested type parameters are not addressable by name.
    /// </summary>
    private static MethodBase AsDeclaredOnGenericDefinition(MethodBase method)
    {
        Type declaringType = method.DeclaringType;
        if (declaringType is not { IsGenericType: true, IsGenericTypeDefinition: false })
        {
            return method;
        }

        return MethodBase.GetMethodFromHandle(
            method.MethodHandle,
            declaringType.GetGenericTypeDefinition().TypeHandle) ?? method;
    }

    private static string GetFormatedTypeName(Type type, bool isMethodParameter, Type contextType)
    {
        if (type.IsGenericParameter)
        {
            return FormatGenericParameter(type, contextType);
        }
        else if (type.HasElementType)
        {
            string elementTypeString = GetFormatedTypeName(type.GetElementType(), isMethodParameter, contextType);

            switch (type)
            {
                case Type when type.IsPointer:
                    return elementTypeString + "*";

                case Type when type.IsByRef:
                    return elementTypeString + "@";

                case Type when type.IsArray:
                    int rank = type.GetArrayRank();
                    string arrayDimensionsString = rank > 1
                        ? $"[{string.Join(",", Enumerable.Repeat("0:", rank))}]"
                        : "[]";
                    return elementTypeString + arrayDimensionsString;

                default:
                    throw new Exception($"{nameof(GetFormatedTypeName)} encountered an unhandled element type: {type}");
            }
        }
        else
        {
            string name = type.IsNested
                ? GetFormatedTypeName(type.DeclaringType, isMethodParameter, contextType) + "."
                : type.Namespace + ".";

            name += isMethodParameter
                ? Regex.Replace(type.Name, @"`\d+", string.Empty)
                : type.Name;

            if (type.IsGenericType && isMethodParameter)
            {
                IEnumerable<string> genericArgs = type.GetGenericArguments()
                    .Select(argument => GetFormatedTypeName(argument, isMethodParameter, contextType));
                name += $"{{{string.Join(",", genericArgs)}}}";
            }

            return name;
        }
    }

    /// <summary>
    /// Encodes a generic parameter the way the C# compiler writes documentation IDs.
    /// Method type parameters use <c>``n</c>. Type parameters use <c>`n</c>, where the index is
    /// their position on the member's declaring type, including parameters inherited from an
    /// enclosing generic type.
    /// </summary>
    private static string FormatGenericParameter(Type parameter, Type contextType)
    {
        if (parameter.DeclaringMethod != null)
        {
            return "``" + parameter.GenericParameterPosition;
        }

        if (contextType != null)
        {
            Type[] typeArguments = contextType.GetGenericArguments();
            for (int i = 0; i < typeArguments.Length; i++)
            {
                if (typeArguments[i] == parameter)
                {
                    return "`" + i;
                }
            }
        }

        return "`" + parameter.GenericParameterPosition;
    }
}
