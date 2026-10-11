using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NuGet.Frameworks;

namespace XMLDoc2Markdown.Utils;

/// <summary>Selects dependency paths without loading them.</summary>
internal class AssemblyResolver
{
    private static readonly NuGetFramework TargetFramework = NuGetFramework.Parse("net10.0");

    private readonly List<string> _searchDirectories = new();

    /// <summary>Creates a resolver that probes the tool directory and the NuGet package cache.</summary>
    public AssemblyResolver()
    {
        this.AddSearchDirectory(AppContext.BaseDirectory);
        this.AddSearchDirectory(Path.Combine(AppContext.BaseDirectory, "libs"));

        string nugetPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(nugetPackagesPath))
        {
            nugetPackagesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        this.AddSearchDirectory(nugetPackagesPath);
    }

    /// <summary>Creates a resolver that probes only the supplied directories.</summary>
    internal AssemblyResolver(params string[] searchDirectories)
    {
        foreach (string directory in searchDirectories)
        {
            this.AddSearchDirectory(directory);
        }
    }

    /// <summary>Adds a directory to search for dependency assemblies.</summary>
    public void AddSearchDirectory(string directory)
    {
        this.AddSearchDirectory(directory, first: false);
    }

    /// <summary>Searches this directory before the tool directory and the package cache.</summary>
    internal void AddSearchDirectoryFirst(string directory)
    {
        this.AddSearchDirectory(directory, first: true);
    }

    private void AddSearchDirectory(string directory, bool first)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (first)
        {
            this._searchDirectories.Insert(0, directory);
        }
        else
        {
            this._searchDirectories.Add(directory);
        }
    }

    /// <summary>
    /// Finds a dependency assembly on disk.
    /// Shared-framework files are left for the default runtime binder.
    /// </summary>
    internal string TryResolveAssemblyPath(System.Reflection.AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName?.Name))
        {
            return null;
        }

        string fileName = assemblyName.Name + ".dll";
        foreach (string directory in this._searchDirectories)
        {
            string packagePath = TryResolveFromPackageCache(directory, assemblyName, fileName);
            if (packagePath is not null)
            {
                return packagePath;
            }

            string directPath = Path.Combine(directory, fileName);
            if (File.Exists(directPath) && !IsSharedFrameworkPath(directPath))
            {
                return directPath;
            }
        }

        return null;
    }

    /// <summary>Returns whether a path belongs to a dotnet shared framework store.</summary>
    internal static bool IsSharedFrameworkPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath = Path.GetFullPath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string marker = $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}Microsoft.";
        return fullPath.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether the running host already supplies this assembly from a shared framework.
    /// A package-cache copy must not be loaded over that host assembly.
    /// </summary>
    internal static bool IsHostSharedFrameworkAssembly(System.Reflection.AssemblyName assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName?.Name))
        {
            return false;
        }

        string fileName = assemblyName.Name + ".dll";
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();
        if (File.Exists(Path.Combine(runtimeDirectory, fileName)))
        {
            return true;
        }

        string version = new DirectoryInfo(runtimeDirectory).Name;
        string sharedRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", ".."));
        foreach (string framework in new[] { "Microsoft.AspNetCore.App", "Microsoft.WindowsDesktop.App" })
        {
            if (File.Exists(Path.Combine(sharedRoot, framework, version, fileName)))
            {
                return true;
            }
        }

        return false;
    }

    private static string TryResolveFromPackageCache(
        string packagesRoot,
        System.Reflection.AssemblyName assemblyName,
        string fileName)
    {
        string packageDirectory = Path.Combine(packagesRoot, assemblyName.Name.ToLowerInvariant());
        if (!Directory.Exists(packageDirectory) || assemblyName.Version is null)
        {
            return null;
        }

        string exactDirectory = Path.Combine(packageDirectory, GetThreeDigitVersion(assemblyName.Version));
        string exactPath = SelectBestFrameworkAssembly(exactDirectory, fileName);
        if (exactPath is not null)
        {
            return exactPath;
        }

        return Directory.GetDirectories(packageDirectory)
            .Select(path => (path, version: TryParseVersion(Path.GetFileName(path))))
            .Where(candidate => candidate.version is not null &&
                                candidate.version.Major == assemblyName.Version.Major)
            .OrderByDescending(candidate => candidate.version)
            .Select(candidate => SelectBestFrameworkAssembly(candidate.path, fileName))
            .FirstOrDefault(path => path is not null);
    }

    private static string SelectBestFrameworkAssembly(string versionDirectory, string fileName)
    {
        string libDirectory = Path.Combine(versionDirectory, "lib");
        if (!Directory.Exists(libDirectory))
        {
            return null;
        }

        var candidates = Directory.GetDirectories(libDirectory)
            .Select(path => (path, framework: NuGetFramework.ParseFolder(Path.GetFileName(path))))
            .Where(candidate => candidate.framework.IsSpecificFramework &&
                                File.Exists(Path.Combine(candidate.path, fileName)))
            .Select(candidate => candidate.path)
            .ToArray();

        if (candidates.Length == 0)
        {
            return null;
        }

        string nearest = NuGetFrameworkUtility.GetNearest(
            candidates,
            TargetFramework,
            path => NuGetFramework.ParseFolder(Path.GetFileName(path)));
        if (nearest is not null)
        {
            return Path.Combine(nearest, fileName);
        }

        // net10.0 cannot NuGet-consume .NET Framework or Windows-only assets, but those
        // assemblies still load for reflection. Prefer a modern Windows target, then
        // the newest .NET Framework asset.
        string fallback = candidates
            .Select(path => (path, framework: NuGetFramework.ParseFolder(Path.GetFileName(path))))
            .Where(candidate => FallbackFamily(candidate.framework) > 0)
            .OrderByDescending(candidate => FallbackFamily(candidate.framework))
            .ThenByDescending(candidate => candidate.framework.Version)
            .Select(candidate => candidate.path)
            .FirstOrDefault();

        return fallback is null ? null : Path.Combine(fallback, fileName);
    }

    private static int FallbackFamily(NuGetFramework framework)
    {
        if (string.Equals(
                framework.Framework,
                FrameworkConstants.FrameworkIdentifiers.NetCoreApp,
                StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (string.Equals(
                framework.Framework,
                FrameworkConstants.FrameworkIdentifiers.Net,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 0;
    }

    private static Version TryParseVersion(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return null;
        }

        string numeric = directoryName.Split('-', 2)[0];
        return Version.TryParse(numeric, out Version version) ? version : null;
    }

    private static string GetThreeDigitVersion(Version version)
    {
        int minor = version.Minor < 0 ? 0 : version.Minor;
        int build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{minor}.{build}";
    }
}
