using System.Collections.Concurrent;
using UnityYamlMerge.Core;
using UnityYamlMerge.Git;
using ValueTaskSupplement;
using EnvironmentVariables = UnityYamlMerge.Git.EnvironmentVariables;

// Parse command line arguments
string? autoPushRemote = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--auto-push" && i + 1 < args.Length)
    {
        autoPushRemote = args[i + 1];
        break;
    }
}

var (targetExtensions, baseBranch) = EnvironmentVariables.Get();
var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

try
{
    await GitHelper.SetConfigSafeDirectoryAsync(cancellationTokenSource.Token);

    if (string.IsNullOrEmpty(baseBranch))
    {
        baseBranch = await GitHelper.GetDefaultBranchAsync(cancellationTokenSource.Token);
    }

    await GitHelper.FetchAsync(baseBranch, cancellationTokenSource.Token);
    var remoteBranch = "origin/" + baseBranch;

    // Detect conflicts when merging headBranch (HEAD) into remoteBranch (e.g., main)
    const string headBranch = "HEAD";
    var conflictFiles = await GitHelper.GetConflictedFilePathsAsync(remoteBranch, headBranch, targetExtensions, cancellationTokenSource.Token);

    if (conflictFiles.Count == 0)
    {
        Console.WriteLine("No conflicts found in target extensions.");
        Environment.Exit(0);
    }

    // Use the common ancestor (merge-base) of ours/theirs as base
    var mergeBase = await GitHelper.GetMergeBaseAsync(remoteBranch, headBranch, cancellationTokenSource.Token);

    var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    Directory.CreateDirectory(tempDir);
    var requests = new ConcurrentQueue<MergeRequest>();

    try
    {
        await Parallel.ForEachAsync(conflictFiles, cancellationTokenSource.Token, async (filePath, token) =>
        {
            var (basePath, oursPath, theirsPath) = GetThreeWayPaths(filePath, tempDir);

            // base = common ancestor, ours = HEAD (PR source), theirs = baseBranch (PR target)
            await ValueTaskEx.WhenAll(
                ExportBlobIfExistsAsync(mergeBase, basePath, filePath, token),
                ExportBlobIfExistsAsync(headBranch, oursPath, filePath, token),
                ExportBlobIfExistsAsync(remoteBranch, theirsPath, filePath, token)
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

        var resolvedFiles = await YamlMergeProcessor.StartAsync(requests, cancellationTokenSource.Token);

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

        // Auto push if AUTO_PUSH_REMOTE is specified
        if (!string.IsNullOrEmpty(autoPushRemote))
        {
            Console.WriteLine("Auto-pushing to remote");
            await GitHelper.AddAsync(resolvedFiles, cancellationTokenSource.Token);
            Console.WriteLine("Added resolved files to staging");

            var message = """
                          Auto-resolve merge conflicts using unity-yaml-merge

                          # Resolved conflicts
                          -
                          """;
            message += string.Join(Environment.NewLine + "- ", resolvedFiles);
            await GitHelper.CommitAsync(message, cancellationTokenSource.Token);
            Console.WriteLine("Committed resolved files");

            await GitHelper.PushAsync(autoPushRemote, cancellationToken: cancellationTokenSource.Token);
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
catch (Exception e)
{
    ThrowHelper.ThrowException(e);
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