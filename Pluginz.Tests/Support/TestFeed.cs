using System.Text;

namespace Pluginz.Tests.Support;

/// <summary>A folder that acts as a NuGet feed. Packages are zip files with a nuspec, written by hand.</summary>
public sealed class TestFeed
{
    public TestFeed()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "pluginz-tests", "feeds", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    /// <summary>Writes <c>{id}.{version}.nupkg</c> into the feed.</summary>
    public string Add(
        string id,
        string version,
        IReadOnlyDictionary<string, byte[]> files,
        IReadOnlyList<(string Id, string Range)>? dependencies = null,
        string tags = "pluginz")
    {
        var nuspec = new StringBuilder()
            .AppendLine("""<?xml version="1.0" encoding="utf-8"?>""")
            .AppendLine("""<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">""")
            .AppendLine("  <metadata>")
            .AppendLine($"    <id>{id}</id>")
            .AppendLine($"    <version>{version}</version>")
            .AppendLine("    <authors>Tests</authors>")
            .AppendLine($"    <description>The {id} package.</description>")
            .AppendLine($"    <tags>{tags}</tags>");

        if (dependencies is { Count: > 0 })
        {
            nuspec.AppendLine("    <dependencies>").AppendLine("""      <group targetFramework="net10.0">""");
            foreach (var (dependencyId, range) in dependencies)
            {
                nuspec.AppendLine($"""        <dependency id="{dependencyId}" version="{range}" />""");
            }

            nuspec.AppendLine("      </group>").AppendLine("    </dependencies>");
        }

        nuspec.AppendLine("  </metadata>").AppendLine("</package>");

        string path = System.IO.Path.Combine(Path, $"{id}.{version}.nupkg");
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        Write(zip, $"{id}.nuspec", Encoding.UTF8.GetBytes(nuspec.ToString()));
        foreach (var (name, content) in files)
        {
            Write(zip, name, content);
        }

        return path;
    }

    /// <summary>Adds a plugin package built from the Hello sample's build output.</summary>
    public string AddHello(string version = "1.0.0", IReadOnlyList<(string Id, string Range)>? dependencies = null)
    {
        string output = TestHost.SampleOutput("Pluginz.Samples.Hello");
        string manifest = File.ReadAllText(System.IO.Path.Combine(output, "plugin.json"));

        return Add(
            "Pluginz.Samples.Hello",
            version,
            new Dictionary<string, byte[]>
            {
                ["lib/net10.0/Pluginz.Samples.Hello.dll"] = File.ReadAllBytes(System.IO.Path.Combine(output, "Pluginz.Samples.Hello.dll")),
                ["pluginz/plugin.json"] = Encoding.UTF8.GetBytes(manifest),
            },
            dependencies ?? [("Newtonsoft.Json", "12.0.3"), ("Pluginz.Core", "1.0.0"), ("NETStandard.Library", "2.0.3")]);
    }

    /// <summary>Adds the Newtonsoft.Json 12 assembly that the Hello sample was built with.</summary>
    public string AddNewtonsoft() => Add(
        "Newtonsoft.Json",
        "12.0.3",
        new Dictionary<string, byte[]>
        {
            ["lib/netstandard2.0/Newtonsoft.Json.dll"] = File.ReadAllBytes(System.IO.Path.Combine(TestHost.SampleOutput("Pluginz.Samples.Hello"), "Newtonsoft.Json.dll")),
        },
        tags: "json");

    private static void Write(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }
}