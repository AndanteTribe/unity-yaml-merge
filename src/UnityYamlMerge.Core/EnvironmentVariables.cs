namespace UnityYamlMerge.Core;

public static class EnvironmentVariables
{
    public const string UnityVersionSource = "UNITY_VERSION_SOURCE";
    public const string UnityVersion = "UNITY_VERSION";
    public const string ProjectPath = "PROJECT_PATH";

    internal static (VersionSource versionSource, string unityVersion, string projectPath) Get()
    {
        var variables = Environment.GetEnvironmentVariables();
        var versionSource = variables.Contains(UnityVersionSource) ? variables[UnityVersionSource]?.ToString() switch
        {
            "project" => VersionSource.Project,
            "latest-lts" => VersionSource.LatestLts,
            "manual" => VersionSource.Manual,
            _ => VersionSource.Invalid
        } : VersionSource.Project;
        var unityVersion = variables.Contains(UnityVersion) ? variables[UnityVersion]?.ToString() ?? "" : "";
        var projectPath = variables.Contains(ProjectPath)
            ? variables[ProjectPath]?.ToString() ?? Path.GetFullPath(".")
            : Path.GetFullPath(".");
        ThrowHelper.ThrowIfMissingEnvironmentVariables(versionSource, unityVersion, projectPath);
        return (versionSource, unityVersion, projectPath);
    }
}