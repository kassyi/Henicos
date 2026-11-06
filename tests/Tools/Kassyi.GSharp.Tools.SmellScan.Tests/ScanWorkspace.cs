#nullable enable
using System.Collections.Immutable;

namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>A temporary directory that stands in for a repository: source files are copied in, then scanned.</summary>
internal sealed class ScanWorkspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smell-scan-" + Guid.NewGuid().ToString("N"));

    public ScanWorkspace()
    {
        Directory.CreateDirectory(_root);
    }

    /// <summary>Gets the directory that plays the repository root.</summary>
    internal string Root => _root;

    /// <summary>Copies the test case <paramref name="caseFile"/> to <paramref name="relativePath"/> in the workspace.</summary>
    internal void Add(string caseFile, string relativePath) => Write(relativePath, TestCaseFiles.Read(caseFile));

    /// <summary>Writes <paramref name="text"/> to <paramref name="relativePath"/> in the workspace.</summary>
    internal void Write(string relativePath, string text)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? _root);
        File.WriteAllText(full, text);
    }

    /// <summary>Scans <paramref name="files"/> with the configuration test case <paramref name="configCase"/>.</summary>
    internal ScanReport Scan(string configCase, int parallelism, params string[] files)
    {
        var configText = TestCaseFiles.Read(configCase);
        var config = SmellConfigReader.Read(configCase, configText);
        var runner = new ScanRunner(_root, config, parallelism);
        return runner.Run([.. files], configCase, configText);
    }

    public void Dispose()
    {
        // git writes its object files read-only, which would stop the deletion on Windows
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, recursive: true);
    }
}
