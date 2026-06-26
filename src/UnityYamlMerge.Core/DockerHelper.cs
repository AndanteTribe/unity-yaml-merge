using System.Diagnostics;
using ValueTaskSupplement;

namespace UnityYamlMerge.Core;

public static class DockerHelper
{
    public static async ValueTask<bool> PullImageAsync(string imageName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = ProcessStartInfo.Create("docker");
        startInfo.ArgumentList.Add("pull");
        startInfo.ArgumentList.Add(imageName);
        var exitCode = await Process.StartAsync(startInfo, cancellationToken: cancellationToken);
        return exitCode == 0;
    }

    internal static async ValueTask<bool> RunMergeAsync(string dockerImage, string projectPath, MergeRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var absBase = Path.GetFullPath(request.Base);
        var absOurs = Path.GetFullPath(request.Ours);
        var absTheirs = Path.GetFullPath(request.Theirs);
        var absOutput = Path.GetFullPath(request.Output);

        var absOutputDir = Path.GetDirectoryName(absOutput);
        if (!string.IsNullOrEmpty(absOutputDir))
        {
            Directory.CreateDirectory(absOutputDir);
        }
        var workDirectory = Path.Combine(projectPath, ".unityyamlmerge", Path.GetRandomFileName());
        Directory.CreateDirectory(workDirectory);

        try
        {
            var baseFileName = Path.GetFileName(absBase);
            var oursFileName = Path.GetFileName(absOurs);
            var theirsFileName = Path.GetFileName(absTheirs);

            await ValueTaskEx.WhenAll(
                CopyAsync(absBase, Path.Combine(workDirectory, baseFileName), cancellationToken),
                CopyAsync(absOurs, Path.Combine(workDirectory, oursFileName), cancellationToken),
                CopyAsync(absTheirs, Path.Combine(workDirectory, theirsFileName), cancellationToken)
            );

            const string unityYamlMerge = "/opt/unity/Editor/Data/Tools/UnityYAMLMerge";

            var startInfo = ProcessStartInfo.Create("docker");
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--rm");
            startInfo.ArgumentList.Add("--entrypoint");
            startInfo.ArgumentList.Add(unityYamlMerge);
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(projectPath + ":/project");
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(workDirectory + ":/merge:ro");
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(absOutputDir + ":/output");
            startInfo.ArgumentList.Add(dockerImage);
            startInfo.ArgumentList.Add("merge");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add("/merge/" + baseFileName);
            startInfo.ArgumentList.Add("/merge/" + oursFileName);
            startInfo.ArgumentList.Add("/merge/" + theirsFileName);
            startInfo.ArgumentList.Add("/output/" + Path.GetFileName(absOutput));
            var exitCode = await Process.StartAsync(startInfo, cancellationToken: cancellationToken);
            return exitCode == 0;
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }

        static async ValueTask CopyAsync(string source, string destination, CancellationToken cancellationToken = default)
        {
            await using var sourceStream = File.OpenRead(source);
            await using var destinationStream = File.Create(destination);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }
    }
}