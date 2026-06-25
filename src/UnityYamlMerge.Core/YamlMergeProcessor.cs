using System.Buffers;

namespace UnityYamlMerge.Core;

public static class YamlMergeProcessor
{
    public static async ValueTask StartAsync(IReadOnlyList<MergeRequest> requests, CancellationToken cancellationToken = default)
    {
        ThrowHelper.ThrowIfInvalidArguments(requests);
        var (versionSource, unityVersion, projectPath) = EnvironmentVariables.Get();
        using var httpClient = new HttpClient();
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        try
        {
            unityVersion = versionSource switch
            {
                VersionSource.Project => GetLocalUnityVersion(projectPath),
                VersionSource.LatestLts => await httpClient.GetLatestLtsVersionAsync(cancellationToken: cancellationToken),
                _ => unityVersion,
            };
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowException(e);
        }
        await ValidateUnityVersionAsync(versionSource, unityVersion, httpClient, cancellationToken);

        var dockerImage = "unityci/editor:" + unityVersion + "-base-3";

        try
        {
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
                }
            }

            if (!pullSuccess)
            {
                ThrowHelper.ThrowFailPullDockerImage(dockerImage);
            }

            await Parallel.ForEachAsync(requests, cancellationToken, async (request, ct) =>
            {
                var success = await DockerHelper.RunMergeAsync(dockerImage, projectPath, request, ct);
                if (!success)
                {
                    throw new InvalidOperationException($"Merge failed for request: {request}");
                }
            });
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowException(e);
        }
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

        try
        {
            if (!await httpClient.CheckValidVersionAsync(unityVersion, cancellationToken: cancellationToken))
            {
                ThrowHelper.ThrowInvalidUnityVersion(unityVersion);
            }
        }
        catch (Exception e)
        {
            ThrowHelper.ThrowException(e);
        }
    }
}