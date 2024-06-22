using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace XMLDoc2Markdown.Utils;

internal sealed class AssemblyResolver
{
    private readonly List<string> _frameworkFolders = new()
    {
        "netstandard2.0",
        "netstandard2.1",
        "net5.0",
        "net6.0",
        "net7.0",
        "net8.0"
    };

    private readonly List<string> _searchDirectories = new();

    public AssemblyResolver()
    {
        // Add default search directories
        this._searchDirectories.Add(AppDomain.CurrentDomain.BaseDirectory);
        this._searchDirectories.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "libs")); // Custom library folder

        // Add NuGet packages directory
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string nugetPackagesDir = Path.Combine(userProfile, ".nuget", "packages");
        this._searchDirectories.Add(nugetPackagesDir);

        // Register the event handler
        AppDomain.CurrentDomain.AssemblyResolve += this.OnAssemblyResolve;
    }

    public void AddSearchDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            this._searchDirectories.Add(directory);
        }
    }

    private Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        AssemblyName assemblyName = new(args.Name);

        // Get the 3-digit version number
        string version = GetThreeDigitVersion(assemblyName.Version);

        foreach (string directory in this._searchDirectories)
        {
            // Build the path for NuGet packages with framework folders
            string packageDir = Path.Combine(directory, assemblyName.Name!.ToLowerInvariant(), version, "lib");
            foreach (Assembly resolvedAssembly in from framework in this._frameworkFolders
                     select Path.Combine(packageDir, framework, assemblyName.Name + ".dll")
                     into assemblyPath
                     where File.Exists(assemblyPath)
                     select Assembly.LoadFrom(assemblyPath))
            {
                return resolvedAssembly;
            }

            // Build the path for general directories (fallback)
            string generalPath = Path.Combine(directory, assemblyName.Name + ".dll");
            if (!File.Exists(generalPath))
            {
                continue;
            }

            return Assembly.LoadFrom(generalPath);
        }

        // If not found, return null
        return null;
    }

    private static string GetThreeDigitVersion(Version version)
    {
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
