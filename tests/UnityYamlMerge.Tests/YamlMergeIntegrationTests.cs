namespace UnityYamlMerge.Tests;

using System.Diagnostics;
using UnityYamlMerge.Core;

public class YamlMergeIntegrationTests : IDisposable
{
    private readonly string _testOutputDirectory;
    private readonly string _testProjectDirectory;
    private readonly string _samplesDirectory;

    public YamlMergeIntegrationTests()
    {
        // Create a temporary directory for test outputs and project path
        var baseTestDir = Path.Combine(Path.GetTempPath(), "UnityYamlMergeTests", Guid.NewGuid().ToString());
        _testOutputDirectory = Path.Combine(baseTestDir, "output");
        _testProjectDirectory = Path.Combine(baseTestDir, "project");
        Directory.CreateDirectory(_testOutputDirectory);
        Directory.CreateDirectory(_testProjectDirectory);

        // Get the samples directory from the test assembly location
        var assemblyLocation = typeof(YamlMergeIntegrationTests).Assembly.Location;
        var assemblyDirectory = Path.GetDirectoryName(assemblyLocation) ?? throw new InvalidOperationException("Could not determine assembly directory");
        _samplesDirectory = Path.Combine(assemblyDirectory, "samples");

        if (!Directory.Exists(_samplesDirectory))
        {
            throw new InvalidOperationException($"Samples directory not found at: {_samplesDirectory}");
        }
    }

    [Fact]
    public async Task StartAsync_WithConflictingSamples_ShouldMergeSuccessfully()
    {
        if (!await IsDockerAvailableAsync())
        {
            throw new InvalidOperationException("Docker is not available. Please ensure Docker is installed and running.");
        }

        // Arrange
        var baseFile = Path.Combine(_samplesDirectory, "Assets", "_merge_base.unity");
        var oursFile = Path.Combine(_samplesDirectory, "Assets", "_merge_ours.unity");
        var theirsFile = Path.Combine(_samplesDirectory, "Assets", "_merge_theirs.unity");
        var outputFile = Path.Combine(_testOutputDirectory, "merged_output.unity");

        // Verify sample files exist
        Assert.True(File.Exists(baseFile), $"Base file not found: {baseFile}");
        Assert.True(File.Exists(oursFile), $"Ours file not found: {oursFile}");
        Assert.True(File.Exists(theirsFile), $"Theirs file not found: {theirsFile}");

        var mergeRequest = new MergeRequest(baseFile, oursFile, theirsFile, outputFile);
        var requests = new List<MergeRequest> { mergeRequest };

        // Act
        try
        {
            var resolvedFiles = await YamlMergeProcessor.StartAsync(requests, CreateOptions());

            // Assert resolved files
            Assert.Single(resolvedFiles);
            Assert.Equal(outputFile, resolvedFiles.First());
        }
        catch (Exception ex)
        {
            throw new Exception($"YamlMergeProcessor.StartAsync failed: {ex.Message}", ex);
        }

        // Assert
        Assert.True(File.Exists(outputFile), "Output file was not created");

        var outputContent = await File.ReadAllTextAsync(outputFile);
        Assert.False(string.IsNullOrWhiteSpace(outputContent), "Output file is empty");

        // Verify it's valid YAML with Unity header
        Assert.StartsWith("%YAML 1.1", outputContent);
        Assert.Contains("%TAG !u! tag:unity3d.com,2011:", outputContent);

        // Verify the file has reasonable content (not just headers)
        var lines = outputContent.Split('\n');
        Assert.True(lines.Length > 10, "Output file seems too short");
    }

    [Fact]
    public async Task StartAsync_WithMultipleRequests_ShouldMergeAllSuccessfully()
    {
        if (!await IsDockerAvailableAsync())
        {
            throw new InvalidOperationException("Docker is not available. Please ensure Docker is installed and running.");
        }

        // Arrange
        var baseFile = Path.Combine(_samplesDirectory, "Assets", "_merge_base.unity");
        var oursFile = Path.Combine(_samplesDirectory, "Assets", "_merge_ours.unity");
        var theirsFile = Path.Combine(_samplesDirectory, "Assets", "_merge_theirs.unity");
        var outputFile1 = Path.Combine(_testOutputDirectory, "merged_output1.unity");
        var outputFile2 = Path.Combine(_testOutputDirectory, "merged_output2.unity");

        var requests = new List<MergeRequest>(2)
        {
            new MergeRequest(baseFile, oursFile, theirsFile, outputFile1),
            new MergeRequest(baseFile, oursFile, theirsFile, outputFile2)
        };

        // Act
        var resolvedFiles = await YamlMergeProcessor.StartAsync(requests, CreateOptions());

        // Assert resolved files
        Assert.Equal(2, resolvedFiles.Count);
        Assert.Contains(outputFile1, resolvedFiles);
        Assert.Contains(outputFile2, resolvedFiles);

        // Assert
        Assert.True(File.Exists(outputFile1), "Output file 1 was not created");
        Assert.True(File.Exists(outputFile2), "Output file 2 was not created");

        var content1 = await File.ReadAllTextAsync(outputFile1);
        var content2 = await File.ReadAllTextAsync(outputFile2);

        Assert.False(string.IsNullOrWhiteSpace(content1), "Output file 1 is empty");
        Assert.False(string.IsNullOrWhiteSpace(content2), "Output file 2 is empty");

        // Both outputs should be identical since they're merging the same files
        Assert.Equal(content1, content2);
    }

    [Fact]
    public async Task StartAsync_WithOutputSameAsOurs_ShouldOverwriteSuccessfully()
    {
        if (!await IsDockerAvailableAsync())
        {
            throw new InvalidOperationException("Docker is not available. Please ensure Docker is installed and running.");
        }

        // Arrange - Copy ours file to output directory so we can modify it
        var baseFile = Path.Combine(_samplesDirectory, "Assets", "_merge_base.unity");
        var oursFile = Path.Combine(_testOutputDirectory, "ours_to_overwrite.unity");
        var theirsFile = Path.Combine(_samplesDirectory, "Assets", "_merge_theirs.unity");

        File.Copy(Path.Combine(_samplesDirectory, "Assets", "_merge_ours.unity"), oursFile, true);
        await File.ReadAllTextAsync(oursFile);

        var mergeRequest = new MergeRequest(baseFile, oursFile, theirsFile, oursFile); // output == ours
        var requests = new List<MergeRequest> { mergeRequest };

        // Act
        var resolvedFiles = await YamlMergeProcessor.StartAsync(requests, CreateOptions());

        // Assert resolved files
        Assert.Single(resolvedFiles);
        Assert.Equal(oursFile, resolvedFiles.First());

        // Assert
        Assert.True(File.Exists(oursFile), "Ours/Output file should still exist");

        var mergedContent = await File.ReadAllTextAsync(oursFile);
        Assert.False(string.IsNullOrWhiteSpace(mergedContent), "Merged file is empty");

        // Verify it's valid YAML
        Assert.StartsWith("%YAML 1.1", mergedContent);

        // The merged content might be different from original ours if there were actual conflicts
        // but we just verify the merge completed and produced valid output
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        // Cleanup: Delete all test directories
        try
        {
            var baseTestDir = Directory.GetParent(_testOutputDirectory)?.FullName;
            if (!string.IsNullOrEmpty(baseTestDir) && Directory.Exists(baseTestDir))
            {
                Directory.Delete(baseTestDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup - ignore errors
        }
    }

    private YamlMergeOptions CreateOptions()
    {
        return new YamlMergeOptions
        {
            VersionSource = VersionSource.LatestLts,
            ProjectPath = _samplesDirectory
        };
    }
}