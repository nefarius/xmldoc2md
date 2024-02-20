using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace XMLDoc2Markdown.Utils;

internal static class AssemblyExtensions
{
    internal static IEnumerable<string> GetDeclaredNamespaces(this Assembly assembly)
    {
        return assembly.GetTypes().Select(type => type.Namespace).Distinct();
    }

    internal static IEnumerable<Type> GetLoadableTypes(this Assembly assembly)
    {
        if (assembly == null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }
}
