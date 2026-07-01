namespace UnityYamlMerge.Core;

public static class ThrowHelper
{
    public static void ThrowIfInvalidOptions(YamlMergeOptions options)
    {
        var errors = new List<string>();
        if (options.VersionSource == VersionSource.Invalid)
        {
            errors.Add("Unity version source is not set or invalid. Set it to one of the following values: `project`, `latest-lts`, or `manual`.");
        }
        if (options.VersionSource == VersionSource.Manual && string.IsNullOrEmpty(options.UnityVersion))
        {
            errors.Add("Unity version is not set. Set it to the desired Unity version when version source is set to `manual`.");
        }
        if (string.IsNullOrEmpty(options.ProjectPath))
        {
            errors.Add("Project path is not set.");
        }
        else
        {
            if (!Directory.Exists(options.ProjectPath))
            {
                errors.Add($"Project path '{options.ProjectPath}' does not exist.");
            }
            if (options.VersionSource == VersionSource.Project && !File.Exists(Path.Combine(options.ProjectPath, "ProjectSettings", "ProjectVersion.txt")))
            {
                errors.Add($"Project path '{options.ProjectPath}' is missing ProjectVersion.txt.");
            }
        }

        if (errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    public static void ThrowIfInvalidArguments(IReadOnlyCollection<MergeRequest> requests)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Parallel.ForEach(requests, request =>
        {
            if (!File.Exists(request.Base))
            {
                errors.Enqueue($"Base file '{request.Base}' does not exist.");
            }
            if (!File.Exists(request.Ours))
            {
                errors.Enqueue($"Ours file '{request.Ours}' does not exist.");
            }
            if (!File.Exists(request.Theirs))
            {
                errors.Enqueue($"Theirs file '{request.Theirs}' does not exist.");
            }
        });

        if (!errors.IsEmpty)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
    }

    public static void ThrowFailGetVersion()
    {
        throw new InvalidOperationException("Failed to get Unity version from project.");
    }

    public static void ThrowInvalidUnityVersion(string unityVersion)
    {
        throw new InvalidOperationException("Invalid Unity version: " + unityVersion);
    }

    public static void ThrowFailPullDockerImage(string dockerImage)
    {
        throw new InvalidOperationException("Failed to pull Docker image: " + dockerImage);
    }
}