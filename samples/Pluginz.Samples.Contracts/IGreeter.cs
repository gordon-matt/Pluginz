namespace Pluginz.Samples;

/// <summary>A contract that lives in an assembly shared by the host and the plugins.</summary>
public interface IGreeter
{
    string Greet(string name);

    /// <summary>Describes the plugin and the version of one library it loaded for itself.</summary>
    string Describe();
}