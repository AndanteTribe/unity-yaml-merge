using ConsoleAppFramework;
using UnityYamlMerge.Core;

await ConsoleApp.RunAsync(args, RunAsync);

static async Task RunAsync(
    string projectPath = ".",
    string unityVersionSource = "project",
    string unityVersion = "",
    CancellationToken cancellationToken = default,
    params string[] files)
{
    try
    {
        if (files.Length == 0)
        {
            Console.WriteLine("No merge requests provided. Exiting.");
            return;
        }

        // arguments: <base1> <ours1> <theirs1> <output1> [<base2> <ours2> <theirs2> <output2> ...]
        if (files.Length % 4 != 0)
        {
            throw new InvalidOperationException("""
                Usage: UnityYamlMerge <base> <ours> <theirs> <output> [...]
                Arguments must be provided in sets of 4.
                """);
        }

        var requests = new List<MergeRequest>(files.Length / 4);
        for (var i = 0; i < files.Length; i += 4)
        {
            requests.Add(new MergeRequest(files[i], files[i + 1], files[i + 2], files[i + 3]));
        }

        var options = new YamlMergeOptions
        {
            VersionSource = YamlMergeOptions.ParseVersionSource(unityVersionSource),
            UnityVersion = unityVersion,
            ProjectPath = Path.GetFullPath(projectPath)
        };

        await YamlMergeProcessor.StartAsync(requests, options, cancellationToken);
    }
    catch (Exception e) when (e is not OperationCanceledException)
    {
        Console.Error.WriteLine(e.Message);
        Environment.ExitCode = 1;
    }
}