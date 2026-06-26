using System.Collections.Concurrent;
using UnityYamlMerge.Core;
using UnityYamlMerge.Git;
using ValueTaskSupplement;
using EnvironmentVariables = UnityYamlMerge.Git.EnvironmentVariables;

var (targetExtensions, baseBranch) = EnvironmentVariables.Get();
var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellationTokenSource.Cancel();
};

if (string.IsNullOrEmpty(baseBranch))
{
    baseBranch = await GitHelper.GetDefaultBranchAsync(cancellationTokenSource.Token);
}

// headBranch (HEAD) を baseBranch (main 等) にマージしたときのコンフリクトを検出する
const string headBranch = "HEAD";
var conflictFiles = await GitHelper.GetConflictedFilePathsAsync(baseBranch, headBranch, targetExtensions, cancellationTokenSource.Token);

if (conflictFiles.Count == 0)
{
    Console.WriteLine("No conflicts found in target extensions.");
    Environment.Exit(0);
}

// ours/theirs の共通祖先 (merge-base) を base として使う
var mergeBase = await GitHelper.GetMergeBaseAsync(baseBranch, headBranch, cancellationTokenSource.Token);

var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
Directory.CreateDirectory(tempDir);
var requests = new ConcurrentQueue<MergeRequest>();

try
{
    await Parallel.ForEachAsync(conflictFiles, cancellationTokenSource.Token, async (filePath, token) =>
    {
        var (basePath, oursPath, theirsPath) = GetThreeWayPaths(filePath, tempDir);

        // base  = 共通祖先, ours = HEAD (PR元), theirs = baseBranch (PR先)
        await ValueTaskEx.WhenAll(
            ExportBlobIfExistsAsync(mergeBase, basePath, filePath, token),
            ExportBlobIfExistsAsync(headBranch, oursPath, filePath, token),
            ExportBlobIfExistsAsync(baseBranch, theirsPath, filePath, token)
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

    await YamlMergeProcessor.StartAsync(requests, cancellationTokenSource.Token);
}
catch (Exception ex)
{
    ThrowHelper.ThrowException(ex);
}
finally
{
    if (Directory.Exists(tempDir))
    {
        Directory.Delete(tempDir, recursive: true);
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