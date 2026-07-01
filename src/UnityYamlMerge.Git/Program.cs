using System.Collections.Concurrent;
using ConsoleAppFramework;
using UnityYamlMerge.Core;
using UnityYamlMerge.Git;
using ValueTaskSupplement;

await ConsoleApp.RunAsync(args, RunAsync);

static async Task RunAsync(
    bool autoPush = false,
    string gitUserEmail = "",
    string gitUserName = "",
    string baseBranch = "",
    string projectPath = ".",
    string unityVersionSource = "project",
    string unityVersion = "",
    CancellationToken cancellationToken = default,
    params string[] targetExtensions)
{
    var options = new YamlMergeOptions
    {
        VersionSource = YamlMergeOptions.ParseVersionSource(unityVersionSource),
        UnityVersion = unityVersion,
        ProjectPath = Path.GetFullPath(projectPath)
    };
    var parsedTargetExtensions = targetExtensions.Length == 0 ? ["unity", "prefab"] : targetExtensions;
    var autoPushRemote = autoPush ? "origin" : null;

    try
    {
        await GitHelper.SetConfigUserAsync(gitUserEmail, gitUserName, cancellationToken);
        await GitHelper.SetConfigSafeDirectoryAsync(cancellationToken);
        if (string.IsNullOrEmpty(baseBranch))
        {
            baseBranch = await GitHelper.GetDefaultBranchAsync(cancellationToken);
        }

        await GitHelper.FetchAsync(baseBranch, cancellationToken);
        var remoteBranch = "origin/" + baseBranch;

        // Detect conflicts when merging headBranch (HEAD) into remoteBranch (e.g., main)
        const string headBranch = "HEAD";
        var allConflictFiles = await GitHelper.GetConflictedFilePathsAsync(remoteBranch, headBranch, cancellationToken: cancellationToken);

        if (allConflictFiles.Count == 0)
        {
            Console.WriteLine("No conflicts found.");
            return;
        }

        var conflictedTargetFiles = GetConflictedTargetFiles(allConflictFiles, parsedTargetExtensions);

        if (conflictedTargetFiles.Count == 0)
        {
            Console.WriteLine("No conflicts found in target extensions.");
            return;
        }

        Console.WriteLine($"Found {allConflictFiles.Count} conflicted files: {string.Join(", ", allConflictFiles)}");
        Console.WriteLine($"Found {conflictedTargetFiles.Count} conflicted files with target extensions: {string.Join(", ", conflictedTargetFiles)}");
        var canFullyResolve = allConflictFiles.Count == conflictedTargetFiles.Count;

        // Use the common ancestor (merge-base) of ours/theirs as base
        var mergeBase = await GitHelper.GetMergeBaseAsync(remoteBranch, headBranch, cancellationToken);

        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        var requests = new ConcurrentQueue<MergeRequest>();

        try
        {
            if (canFullyResolve && !string.IsNullOrEmpty(autoPushRemote))
            {
                await GitHelper.MergeAsync(remoteBranch, cancellationToken);
                Console.WriteLine("Merge started");
            }

            await Parallel.ForEachAsync(conflictedTargetFiles, cancellationToken, async (filePath, token) =>
            {
                var (basePath, oursPath, theirsPath) = GetThreeWayPaths(filePath, tempDir);
                var oursRevision = canFullyResolve ? "ORIG_HEAD" : headBranch;
                var theirsRevision = canFullyResolve ? "MERGE_HEAD" : remoteBranch;

                // base = common ancestor, ours = HEAD (PR source), theirs = baseBranch (PR target)
                await ValueTaskEx.WhenAll(
                    ExportBlobIfExistsAsync(mergeBase, basePath, filePath, token),
                    ExportBlobIfExistsAsync(oursRevision, oursPath, filePath, token),
                    ExportBlobIfExistsAsync(theirsRevision, theirsPath, filePath, token)
                );

                requests.Enqueue(new MergeRequest(basePath, oursPath, theirsPath, filePath));

                static async ValueTask ExportBlobIfExistsAsync(string revision, string outputPath, string filePath, CancellationToken cancellationToken)
                {
                    var oid = await GitHelper.GetBlobOidAsync(revision, filePath, cancellationToken);

                    if (string.IsNullOrEmpty(oid))
                    {
                        await File.WriteAllBytesAsync(outputPath, [], cancellationToken);
                    }
                    else
                    {
                        await GitHelper.ExportBlobAsync(oid, outputPath, cancellationToken);
                    }
                }
            });

            var resolvedFiles = await YamlMergeProcessor.StartAsync(requests, options, cancellationToken);

            if (resolvedFiles.Count <= 0)
            {
                Console.WriteLine("# No files were resolved.");
                return;
            }

            // Output resolved file paths to stdout (one per line)
            Console.WriteLine("# Resolved files:");
            foreach (var file in resolvedFiles)
            {
                Console.WriteLine(file);
            }

            if (!string.IsNullOrEmpty(autoPushRemote))
            {
                Console.WriteLine("Auto-pushing to remote");
                var message = """
                              Auto-resolve merge conflicts using unity-yaml-merge

                              # Resolved conflicts
                              -
                              """ + string.Join(Environment.NewLine + "- ", resolvedFiles);

                await GitHelper.AddAsync(resolvedFiles, cancellationToken);
                Console.WriteLine("Added resolved files to staging");

                if (canFullyResolve && resolvedFiles.Count == allConflictFiles.Count)
                {
                    try
                    {
                        await GitHelper.MergeContinueAsync(cancellationToken);
                        Console.WriteLine("Merge completed");
                    }
                    catch
                    {
                        await GitHelper.MergeAbortAsync(cancellationToken);
                        throw;
                    }
                }
                else
                {
                    // partial resolution, commit
                    await GitHelper.CommitAsync(message, cancellationToken);
                    Console.WriteLine("Committed resolved files");
                }

                await GitHelper.PushAsync(autoPushRemote, cancellationToken: cancellationToken);
                Console.WriteLine("Successfully pushed resolved files to remote");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
        Console.Error.WriteLine(e.Message);
        Environment.ExitCode = 1;
    }
}


static (string, string, string) GetThreeWayPaths(ReadOnlySpan<char> filePath, string tempDir)
{
    var safeRelative = (Span<char>)stackalloc char[filePath.Length];
    filePath.Replace(safeRelative, '/', Path.DirectorySeparatorChar);
    var fileDir = Path.Combine(tempDir, Path.GetDirectoryName(safeRelative).ToString());
    Directory.CreateDirectory(fileDir);

    var fileName = Path.GetFileName(filePath);
    var basePath = Path.Combine(fileDir, $"base_{fileName}");
    var oursPath = Path.Combine(fileDir, $"ours_{fileName}");
    var theirsPath = Path.Combine(fileDir, $"theirs_{fileName}");
    return (basePath, oursPath, theirsPath);
}

static IReadOnlyList<string> GetConflictedTargetFiles(IReadOnlyList<string> allConflictFiles, ReadOnlySpan<string> targetExtensions)
{
    var conflictedTargetFiles = new List<string>();
    for (var i = 0; i < allConflictFiles.Count; i++)
    {
        var file = allConflictFiles[i];
        foreach (var targetExtension in targetExtensions)
        {
            var extension = Path.GetExtension(file.AsSpan()).TrimStart('.');
            if (targetExtension.AsSpan().SequenceEqual(extension))
            {
                conflictedTargetFiles.Add(file);
                break;
            }
        }
    }
    return conflictedTargetFiles;
}