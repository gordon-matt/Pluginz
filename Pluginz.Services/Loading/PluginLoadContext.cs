using System.Runtime.InteropServices;

namespace Pluginz.Loading;

/// <summary>
/// One load context per plugin. Managed, native and satellite assemblies are resolved from the plugin's folder using
/// its <c>deps.json</c> when it has one, or by probing the folder when it does not (as for NuGet-installed plugins).
/// Assemblies that <see cref="SharedAssemblyRules"/> marks as shared come from the host.
/// </summary>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver resolver;
    private readonly SharedAssemblyRules rules;
    private readonly IReadOnlyCollection<string> privateAssemblies;
    private readonly string directory;

    public PluginLoadContext(
        string systemName,
        string mainAssemblyPath,
        SharedAssemblyRules rules,
        IReadOnlyCollection<string> privateAssemblies,
        bool isCollectible = false)
        : base($"Pluginz:{systemName}", isCollectible)
    {
        SystemName = systemName;
        MainAssemblyPath = mainAssemblyPath;
        directory = Path.GetDirectoryName(mainAssemblyPath)!;
        resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        this.rules = rules;
        this.privateAssemblies = privateAssemblies;
    }

    public string SystemName { get; }

    public string MainAssemblyPath { get; }

    /// <summary>Loads the plugin's main assembly into this context.</summary>
    public Assembly LoadMainAssembly() => LoadFromAssemblyPath(MainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (rules.IsShared(name, privateAssemblies))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
            {
                // The host does not have it, or has an older version. Fall through to the plugin's own copy.
            }
        }

        var path = resolver.ResolveAssemblyToPath(assemblyName) ?? ProbeManaged(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName) ?? ProbeNative(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    private string? ProbeManaged(AssemblyName assemblyName)
    {
        var fileName = $"{assemblyName.Name}.dll";

        // Satellite assemblies sit in a folder named after the culture.
        var candidate = string.IsNullOrEmpty(assemblyName.CultureName) || assemblyName.CultureName == "neutral"
            ? Path.Combine(directory, fileName)
            : Path.Combine(directory, assemblyName.CultureName, $"{assemblyName.Name}.dll");

        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Package layouts keep libraries one level down, under runtimes/{rid}/lib/{tfm}/.
        foreach (var rid in RuntimeIdentifiers())
        {
            var runtimeLib = Path.Combine(directory, "runtimes", rid, "lib");
            if (!Directory.Exists(runtimeLib))
            {
                continue;
            }

            var match = Directory.EnumerateFiles(runtimeLib, fileName, SearchOption.AllDirectories).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private string? ProbeNative(string unmanagedDllName)
    {
        var names = NativeFileNames(unmanagedDllName).ToList();
        var folders = new List<string> { directory };
        folders.AddRange(RuntimeIdentifiers().Select(rid => Path.Combine(directory, "runtimes", rid, "native")));

        return folders
            .Where(Directory.Exists)
            .SelectMany(folder => names.Select(name => Path.Combine(folder, name)))
            .FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> NativeFileNames(string name)
    {
        yield return name;

        var (prefix, extension) = NativeAffixes();
        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{name}{extension}";
            yield return $"{prefix}{name}{extension}";
        }
        else if (prefix.Length > 0 && !name.StartsWith(prefix, StringComparison.Ordinal))
        {
            yield return $"{prefix}{name}";
        }
    }

    private static (string Prefix, string Extension) NativeAffixes()
    {
        if (OperatingSystem.IsWindows())
        {
            return (string.Empty, ".dll");
        }

        return OperatingSystem.IsMacOS() ? ("lib", ".dylib") : ("lib", ".so");
    }

    private static IEnumerable<string> RuntimeIdentifiers()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        yield return RuntimeInformation.RuntimeIdentifier;
        yield return $"{os}-{architecture}";
        yield return os;
    }
}
