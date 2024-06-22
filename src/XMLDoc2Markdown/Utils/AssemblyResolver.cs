using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NuGet.Frameworks;

namespace XMLDoc2Markdown.Utils;

/// <summary>
///     Utility class to dynamically resolve missing assemblies from well-known locations.
/// </summary>
internal sealed class AssemblyResolver
{
    private readonly List<string> _frameworkFolders = new();

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

        // Add ASP.NET Core directory
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        DirectoryInfo aspNetCoreAppPath = new(Path.Combine(runtimeDirectory, "..", "..", "Microsoft.AspNetCore.App"));
        this._searchDirectories.Add(aspNetCoreAppPath.FullName);

        List<NuGetFramework> knownFrameworks =
        [
            NuGetFramework.Parse("netstandard1.0"),
            NuGetFramework.Parse("netstandard1.1"),
            NuGetFramework.Parse("netstandard1.2"),
            NuGetFramework.Parse("netstandard1.3"),
            NuGetFramework.Parse("netstandard1.4"),
            NuGetFramework.Parse("netstandard1.5"),
            NuGetFramework.Parse("netstandard1.6"),
            NuGetFramework.Parse("netstandard2.0"),
            NuGetFramework.Parse("netstandard2.1"),
            NuGetFramework.Parse("netcoreapp1.0"),
            NuGetFramework.Parse("netcoreapp1.1"),
            NuGetFramework.Parse("netcoreapp2.0"),
            NuGetFramework.Parse("netcoreapp2.1"),
            NuGetFramework.Parse("netcoreapp2.2"),
            NuGetFramework.Parse("netcoreapp3.0"),
            NuGetFramework.Parse("netcoreapp3.1"),
            NuGetFramework.Parse("net5.0"),
            NuGetFramework.Parse("net6.0"),
            NuGetFramework.Parse("net7.0"),
            NuGetFramework.Parse("net8.0"),
            NuGetFramework.Parse(".NETFramework,Version=v4.5"),
            NuGetFramework.Parse(".NETFramework,Version=v4.5.1"),
            NuGetFramework.Parse(".NETFramework,Version=v4.5.2"),
            NuGetFramework.Parse(".NETFramework,Version=v4.6"),
            NuGetFramework.Parse(".NETFramework,Version=v4.6.1"),
            NuGetFramework.Parse(".NETFramework,Version=v4.6.2"),
            NuGetFramework.Parse(".NETFramework,Version=v4.7"),
            NuGetFramework.Parse(".NETFramework,Version=v4.7.1"),
            NuGetFramework.Parse(".NETFramework,Version=v4.7.2"),
            NuGetFramework.Parse(".NETFramework,Version=v4.8")
        ];

        this._frameworkFolders.AddRange(knownFrameworks.Select(f => f.GetShortFolderName()));

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

    [SuppressMessage("ReSharper", "AccessToModifiedClosure")]
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

            // Try to find best matching copy based on major version
            packageDir = Path.Combine(directory, assemblyName.Name!.ToLowerInvariant());
            if (Directory.Exists(packageDir))
            {
                IOrderedEnumerable<DirectoryInfo> matchingMajorVersionDirs = Directory.GetDirectories(packageDir)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => d.Name.StartsWith($"{assemblyName.Version!.Major}."))
                    .OrderByDescending(d => d.Name);

                DirectoryInfo versionedSubDir = matchingMajorVersionDirs.First();

                string libPath = Path.Combine(versionedSubDir.Name, "lib");
                foreach (Assembly resolvedAssembly in from framework in this._frameworkFolders
                         select Path.Combine(packageDir, libPath, framework, assemblyName.Name + ".dll")
                         into assemblyPath
                         where File.Exists(assemblyPath)
                         select Assembly.LoadFrom(assemblyPath))
                {
                    return resolvedAssembly;
                }
            }

            // Probe ASP.NET Core
            if (directory.Contains("Microsoft.AspNetCore") && Directory.Exists(directory))
            {
                IOrderedEnumerable<DirectoryInfo> matchingMajorVersionDirs = Directory.GetDirectories(directory)
                    .Select(d => new DirectoryInfo(d))
                    .Where(d => d.Name.StartsWith($"{assemblyName.Version!.Major}."))
                    .OrderByDescending(d => d.Name);

                DirectoryInfo versionedSubDir = matchingMajorVersionDirs.First();

                string assemblyPath = Path.Combine(versionedSubDir.FullName, assemblyName.Name + ".dll");
                if (File.Exists(assemblyPath))
                {
                    return Assembly.LoadFrom(assemblyPath);
                }
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
