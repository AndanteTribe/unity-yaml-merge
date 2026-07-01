using System.Buffers;

namespace UnityYamlMerge.Core;

public static class YamlMergeProcessor
{
    public static async ValueTask<IReadOnlyCollection<string>> StartAsync(IReadOnlyCollection<MergeRequest> requests, YamlMergeOptions options, CancellationToken cancellationToken = default)
    {
        ThrowHelper.ThrowIfInvalidArguments(requests);
        ThrowHelper.ThrowIfInvalidOptions(options);
        var versionSource = options.VersionSource;
        var unityVersion = options.UnityVersion;
        var projectPath = options.ProjectPath;
        using var httpClient = new HttpClient();
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        unityVersion = versionSource switch
        {
            VersionSource.Project => GetLocalUnityVersion(projectPath),
            VersionSource.LatestLts => await httpClient.GetLatestLtsVersionAsync(cancellationToken: cancellationToken),
            _ => unityVersion,
        };
        await ValidateUnityVersionAsync(versionSource, unityVersion, httpClient, cancellationToken);

        var dockerImage = "unityci/editor:" + unityVersion + "-base-3";
        var pullSuccess = await DockerHelper.PullImageAsync(dockerImage, cancellationToken);
        if (!pullSuccess && versionSource == VersionSource.LatestLts)
        {
            using var _ = ArrayPool<string>.Shared.Rent(4, out var excludeVersions);
            excludeVersions.AsSpan().Fill("");
            excludeVersions[0] = unityVersion;
            for (var i = 1; i < excludeVersions.Length; i++)
            {
                unityVersion = await httpClient.GetLatestLtsVersionAsync(excludeVersions, cancellationToken);
                dockerImage = "unityci/editor:" + unityVersion + "-base-3";
                pullSuccess = await DockerHelper.PullImageAsync(dockerImage, cancellationToken);
                if (pullSuccess)
                {
                    break;
                }
                excludeVersions[i] = unityVersion;
            }
        }

        if (!pullSuccess)
        {
            ThrowHelper.ThrowFailPullDockerImage(dockerImage);
        }

        var resolvedFiles = new System.Collections.Concurrent.ConcurrentQueue<string>();
        await Parallel.ForEachAsync(requests, cancellationToken, async (request, ct) =>
        {
            var success = await DockerHelper.RunMergeAsync(dockerImage, projectPath, request, ct);
            if (success)
            {
                resolvedFiles.Enqueue(request.Output);
            }
            // Note: We don't throw exception on failure to allow partial resolution
        });

        return resolvedFiles;
    }

    private static string GetLocalUnityVersion(string projectPath)
    {
        const string prefix = "m_EditorVersion:";

        var versionFilePath = Path.Combine(projectPath, "ProjectSettings", "ProjectVersion.txt");
        var version = File.ReadLines(versionFilePath)
            .FirstOrDefault(static line => line.StartsWith(prefix, StringComparison.Ordinal))?.AsSpan(prefix.Length).Trim().ToString();
        if (string.IsNullOrEmpty(version))
        {
            ThrowHelper.ThrowFailGetVersion();
            return "";
        }
        return version;
    }

    private static async ValueTask ValidateUnityVersionAsync(VersionSource source, string unityVersion, HttpClient httpClient, CancellationToken cancellationToken = default)
    {
        if (source == VersionSource.LatestLts)
        {
            // If the version source is latest LTS, we assume the version is valid since it was retrieved from Unity's official source.
            return;
        }

        if (!await httpClient.CheckValidVersionAsync(unityVersion, cancellationToken: cancellationToken))
        {
            ThrowHelper.ThrowInvalidUnityVersion(unityVersion);
        }
    }
}