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
        string dependencyPath = this._dependencyResolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrEmpty(dependencyPath))
        {
            return AssemblyResolver.IsSharedFrameworkPath(dependencyPath)
                ? null
                : this.LoadFromAssemblyPath(dependencyPath);
        }

        string fallbackPath = this._assemblyResolver.TryResolveAssemblyPath(assemblyName);
        if (string.IsNullOrEmpty(fallbackPath)
            || AssemblyResolver.IsSharedFrameworkPath(fallbackPath)
            || AssemblyResolver.IsHostSharedFrameworkAssembly(assemblyName))
        {
            return null;
        }

        return this.LoadFromAssemblyPath(fallbackPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string libraryPath = this._dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is not null
            ? this.LoadUnmanagedDllFromPath(libraryPath)
            : IntPtr.Zero;
    }
}
