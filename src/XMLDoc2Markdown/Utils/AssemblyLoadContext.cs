using System;
using System.Reflection;
using System.Runtime.Loader;

namespace XMLDoc2Markdown.Utils;

/// <summary>Loads a target assembly and its non-framework dependencies in one context.</summary>
internal class AssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _dependencyResolver;
    private readonly AssemblyResolver _assemblyResolver;

    public AssemblyLoadContext(string assemblyPath, AssemblyResolver assemblyResolver)
        : base(isCollectible: false)
    {
        this._dependencyResolver = new AssemblyDependencyResolver(assemblyPath);
        this._assemblyResolver = assemblyResolver ?? throw new ArgumentNullException(nameof(assemblyResolver));
    }

    protected override Assembly Load(AssemblyName assemblyName)
    {
        string path = this._dependencyResolver.ResolveAssemblyToPath(assemblyName)
            ?? this._assemblyResolver.TryResolveAssemblyPath(assemblyName);

        if (string.IsNullOrEmpty(path) || AssemblyResolver.IsSharedFrameworkPath(path))
        {
            return null;
        }

        return this.LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string libraryPath = this._dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null
            ? this.LoadUnmanagedDllFromPath(libraryPath)
            : IntPtr.Zero;
    }
}
