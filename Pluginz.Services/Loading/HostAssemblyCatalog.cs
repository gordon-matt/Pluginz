using System.Runtime.InteropServices;

namespace Pluginz.Loading;

/// <summary>
/// The assemblies the host process was started with: the shared framework and everything in the host's
/// <c>deps.json</c>. The runtime lists them in <c>TRUSTED_PLATFORM_ASSEMBLIES</c>.
/// </summary>
public sealed class HostAssemblyCatalog
{
    private readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? frameworkRoot;

    private HostAssemblyCatalog()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
        {
            foreach (string path in list.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                paths.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        // .../dotnet/shared/Microsoft.NETCore.App/10.0.x/  ->  .../dotnet/shared/
        string runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        frameworkRoot = Directory.GetParent(Path.GetDirectoryName(runtimeDirectory) ?? runtimeDirectory)?.FullName;
    }

    /// <summary>The catalog for the running process.</summary>
    public static HostAssemblyCatalog Current { get; } = new();

    /// <summary>Whether the host ships an assembly with this simple name.</summary>
    public bool Provides(string assemblyName) => paths.ContainsKey(assemblyName);

    /// <summary>Whether the assembly belongs to a shared framework such as Microsoft.NETCore.App or Microsoft.AspNetCore.App.</summary>
    public bool IsFramework(string assemblyName) =>
        frameworkRoot is not null
        && paths.TryGetValue(assemblyName, out string? path)
        && path.StartsWith(frameworkRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>The file version of the host's copy of an assembly, or null when the host does not have it.</summary>
    public Version? GetVersion(string assemblyName)
    {
        if (!paths.TryGetValue(assemblyName, out string? path))
        {
            return null;
        }

        try
        {
            return AssemblyName.GetAssemblyName(path).Version;
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileNotFoundException or IOException)
        {
            return null;
        }
    }
}