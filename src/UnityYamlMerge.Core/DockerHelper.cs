using System.Diagnostics;

namespace UnityYamlMerge.Core;

public static class DockerHelper
{
    public static ValueTask<bool> PullImageAsync(string imageName, CancellationToken cancellationToken = default)
    {
        return RunAsync("pull " + imageName, cancellationToken);
    }

    internal static async ValueTask<bool> RunMergeAsync(string dockerImage, string projectPath, MergeRequest request, CancellationToken cancellationToken = default)
    {
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

            await Task.WhenAll(
                CopyAsync(absBase, Path.Combine(workDirectory, baseFileName), cancellationToken),
                CopyAsync(absOurs, Path.Combine(workDirectory, oursFileName), cancellationToken),
                CopyAsync(absTheirs, Path.Combine(workDirectory, theirsFileName), cancellationToken)
            );

            const string unityYamlMerge = "/opt/unity/Editor/Data/Tools/UnityYAMLMerge";

            var dockerArgs =
                $"run --rm " +
                $"--entrypoint {unityYamlMerge} " +
                $"-v \"{projectPath}:/project\" " +
                $"-v \"{workDirectory}:/merge:ro\" " +
                $"-v \"{absOutputDir}:/output\" " +
                $"{dockerImage} " +
                $"merge -p /merge/{baseFileName} /merge/{oursFileName} /merge/{theirsFileName} /output/{Path.GetFileName(absOutput)}";

            return await RunAsync(dockerArgs, cancellationToken);
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }

        static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken = default)
        {
            var text = await File.ReadAllTextAsync(source, cancellationToken);
            await File.WriteAllTextAsync(destination, text, cancellationToken);
        }
    }

    private static async ValueTask<bool> RunAsync(string arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start docker process.");
        var stdoutCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutCompletionSource.TrySetResult();
                return;
            }

            Console.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrCompletionSource.TrySetResult();
                return;
            }

            Console.Error.WriteLine(e.Data);
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(stdoutCompletionSource.Task, stderrCompletionSource.Task);
        return process.ExitCode == 0;
    }
}