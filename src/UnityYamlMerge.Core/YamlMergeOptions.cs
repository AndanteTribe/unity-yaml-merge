namespace UnityYamlMerge.Core;

public sealed record YamlMergeOptions
{
    public VersionSource VersionSource { get; init; } = VersionSource.Project;

    public string UnityVersion { get; init; } = "";

    public string ProjectPath { get; init; } = Path.GetFullPath(".");

    public static VersionSource ParseVersionSource(string value)
    {
        return value switch
        {
            "" or "project" => VersionSource.Project,
            "latest-lts" => VersionSource.LatestLts,
            "manual" => VersionSource.Manual,
            _ => VersionSource.Invalid
        };
    }
}