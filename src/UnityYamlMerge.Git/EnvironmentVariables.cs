namespace UnityYamlMerge.Git;

public static class EnvironmentVariables
{
    public const string TargetExtensions = "TARGET_EXTENSIONS";
    public const string BaseBranch = "BASE_BRANCH";

    internal static (string[] targetExtensions, string baseBranch) Get()
    {
        var variables = Environment.GetEnvironmentVariables();
        var targetExtensions = variables.Contains(TargetExtensions)
            ? variables[TargetExtensions]?.ToString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? ["unity", "prefab"]
            : ["unity", "prefab"];
        var baseBranch = variables.Contains(BaseBranch) ? variables[BaseBranch]?.ToString() ?? "" : "";
        return (targetExtensions, baseBranch);
    }
}