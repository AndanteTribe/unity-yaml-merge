using System.Collections.Concurrent;
using System.Diagnostics;
using UnityYamlMerge.Core;
using ValueTaskSupplement;

namespace UnityYamlMerge.Git;

public static class GitHelper
{
    public static async ValueTask SetConfigSafeDirectoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("config");
        processStartInfo.ArgumentList.Add("--global");
        processStartInfo.ArgumentList.Add("--add");
        processStartInfo.ArgumentList.Add("safe.directory");
        processStartInfo.ArgumentList.Add("*");

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    public static async ValueTask SetConfigUserAsync(string? gitUserEmail, string? gitUserName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(gitUserEmail) || string.IsNullOrWhiteSpace(gitUserName))
        {
            return;
        }

        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("config");
        processStartInfo.ArgumentList.Add("--global");
        processStartInfo.ArgumentList.Add("user.email");
        processStartInfo.ArgumentList.Add(gitUserEmail);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
        output.Clear();

        processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("config");
        processStartInfo.ArgumentList.Add("--global");
        processStartInfo.ArgumentList.Add("user.name");
        processStartInfo.ArgumentList.Add(gitUserName);
        exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    public static async ValueTask<string> GetDefaultBranchAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Automatically set `refs/remotes/origin/HEAD` in case it is not set
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("remote");
        processStartInfo.ArgumentList.Add("set-head");
        processStartInfo.ArgumentList.Add("origin");
        processStartInfo.ArgumentList.Add("--auto");
        await Process.StartAsync(processStartInfo, cancellationToken: cancellationToken);

        processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("symbolic-ref");
        processStartInfo.ArgumentList.Add("--short");
        processStartInfo.ArgumentList.Add("refs/remotes/origin/HEAD");

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }

        if (!output.TryDequeue(out var result))
        {
            throw new InvalidOperationException("git returned empty result.");
        }
        result = result.Trim();

        if (string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException("failed to get default branch.");
        }

        // origin/main → main
        const string prefix = "origin/";
        if (result.StartsWith(prefix, StringComparison.Ordinal))
        {
            return result.AsSpan()[prefix.Length..].ToString();
        }

        return result;
    }

    public static async ValueTask FetchAsync(string branch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isShallow = await GetIsShallowAsync(cancellationToken);

        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("fetch");
        if (isShallow)
        {
            processStartInfo.ArgumentList.Add("--unshallow");
        }
        processStartInfo.ArgumentList.Add("origin");
        processStartInfo.ArgumentList.Add(branch);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    /// <summary>
    /// Executes git merge-tree --write-tree and returns a list of conflicted file paths with target extensions.
    /// Detects conflicts when merging targetBranch (PR source, HEAD) into baseBranch (PR target, e.g., main).
    /// </summary>
    public static async ValueTask<IReadOnlyList<string>> GetConflictedFilePathsAsync(
        string baseBranch, string headBranch, ReadOnlyMemory<string> targetExtensions = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // exit code 0 = clean merge, 1 = conflicts exist, other = error
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("merge-tree");
        processStartInfo.ArgumentList.Add("--write-tree");
        processStartInfo.ArgumentList.Add("--name-only");
        processStartInfo.ArgumentList.Add("--no-messages");
        processStartInfo.ArgumentList.Add(baseBranch);
        processStartInfo.ArgumentList.Add(headBranch);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);

        if (exitCode == 0 || output.Count <= 1)
        {
            return [];
        }
        if (exitCode != 1)
        {
            ThrowGitFailed(exitCode, output);
        }

        // Output: line 0 = <tree-oid>, lines 1+ = conflicted file paths
        var files = new List<string>();
        // Discard the tree-oid
        output.TryDequeue(out _);

        while (output.TryDequeue(out var l))
        {
            var line = l.AsSpan().Trim();
            if (line.IsEmpty)
            {
                continue;
            }

            var ext = Path.GetExtension(line).TrimStart('.');
            foreach (var targetExtension in targetExtensions.Span)
            {
                if (targetExtension.AsSpan().SequenceEqual(ext))
                {
                    files.Add(line.ToString());
                    break;
                }
            }
        }
        return files;
    }

    public static async ValueTask<string> GetMergeBaseAsync(string branch1, string branch2, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("merge-base");
        processStartInfo.ArgumentList.Add(branch1);
        processStartInfo.ArgumentList.Add(branch2);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
        return output.TryDequeue(out var line) ? line.Trim() : throw new InvalidOperationException("Could not determine merge base.");
    }

    public static async ValueTask MergeAsync(string branch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("merge");
        processStartInfo.ArgumentList.Add("--no-edit");
        processStartInfo.ArgumentList.Add(branch);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0 && exitCode != 1)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    public static async ValueTask MergeContinueAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("merge");
        processStartInfo.ArgumentList.Add("--continue");
        processStartInfo.ArgumentList.Add("--no-edit");

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    public static async ValueTask MergeAbortAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("merge");
        processStartInfo.ArgumentList.Add("--abort");

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    public static async ValueTask<bool> GetIsShallowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("rev-parse");
        processStartInfo.ArgumentList.Add("--is-shallow-repository");

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
        return output.TryDequeue(out var line) ? line.Trim() == "true" : throw new InvalidOperationException("Could not determine if repository is shallow.");
    }

    public static async ValueTask<string> GetBlobOidAsync(string treeish, string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var processStartInfo = ProcessStartInfo.Create("git");
            processStartInfo.ArgumentList.Add("rev-parse");
            processStartInfo.ArgumentList.Add(treeish + ":" + filePath);

            var output = new ConcurrentQueue<string>();
            var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
            if (exitCode != 0)
            {
                ThrowGitFailed(exitCode, output);
            }
            return output.TryDequeue(out var line) ? line : "";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Exports a git object as binary to a file. Does not use WriteAllLines,
    /// copies directly from StandardOutput.BaseStream to prevent line ending conversion.
    /// </summary>
    public static async ValueTask ExportBlobAsync(string blobOid, string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = ProcessStartInfo.Create("git");
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("blob");
        startInfo.ArgumentList.Add(blobOid);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git process.");

        var stderrCompletionSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var _ = cancellationToken.UnsafeRegister(static args =>
        {
            var (process, stderrCompletionSource) = ((Process, TaskCompletionSource))args!;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Ignore exceptions when killing the process
            }
            stderrCompletionSource.TrySetCanceled();
        }, (process, stderrCompletionSource));

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null)
            {
                stderrCompletionSource.TrySetResult();
                return;
            }
            Console.Error.WriteLine(e.Data);
        };
        process.BeginErrorReadLine();

        await using (var fileStream = File.Create(outputPath))
        {
            await process.StandardOutput.BaseStream.CopyToAsync(fileStream, cancellationToken);
        }

        await Task.WhenAll(stderrCompletionSource.Task, process.WaitForExitAsync(cancellationToken));

        if (process.ExitCode != 0)
        {
            try
            {
                File.Delete(outputPath);
            }
            catch
            {
                // Ignore exceptions when deleting the file
            }
            throw new InvalidOperationException($"git cat-file blob failed for OID: {blobOid}");
        }
    }

    /// <summary>
    /// Executes git add to stage the specified file paths.
    /// </summary>
    public static async ValueTask AddAsync(IEnumerable<string> filePaths, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("add");
        processStartInfo.ArgumentList.Add("--");
        foreach (var filePath in filePaths)
        {
            processStartInfo.ArgumentList.Add(filePath);
        }

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    /// <summary>
    /// Executes git commit.
    /// </summary>
    public static async ValueTask CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("commit");
        processStartInfo.ArgumentList.Add("-m");
        processStartInfo.ArgumentList.Add(message);

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    /// <summary>
    /// Executes git push.
    /// </summary>
    public static async ValueTask PushAsync(string remote, string? refspec = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processStartInfo = ProcessStartInfo.Create("git");
        processStartInfo.ArgumentList.Add("push");
        processStartInfo.ArgumentList.Add("--no-verify");
        processStartInfo.ArgumentList.Add(remote);
        if (!string.IsNullOrEmpty(refspec))
        {
            processStartInfo.ArgumentList.Add(refspec);
        }

        var output = new ConcurrentQueue<string>();
        var exitCode = await Process.StartAsync(processStartInfo, output, cancellationToken);
        if (exitCode != 0)
        {
            ThrowGitFailed(exitCode, output);
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowGitFailed(int exitCode, IEnumerable<string> output)
    {
        throw new InvalidOperationException("git failed (exit code " + exitCode + "): " + string.Join(Environment.NewLine, output));
    }
}