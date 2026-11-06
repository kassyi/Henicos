namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The parsed command line of the tool.</summary>
internal sealed class CommandLineOptions
{
    /// <summary>Gets or sets the repository root; discovered from the current directory when not given.</summary>
    public string? Root { get; set; }

    /// <summary>Gets or sets the smells file; <c>tools/smell-scan/smells.yml</c> of the root when not given.</summary>
    public string? Config { get; set; }

    /// <summary>Gets or sets the JSON file that lists the files to scan.</summary>
    public string? FilesFrom { get; set; }

    /// <summary>Gets or sets the file the report is written to; standard output when not given.</summary>
    public string? Out { get; set; }

    /// <summary>Gets or sets the degree of parallelism; the number of processors when not given.</summary>
    public int? Parallelism { get; set; }

    /// <summary>Gets or sets a value indicating whether help was requested.</summary>
    public bool Help { get; set; }

    /// <summary>Gets the files and directories to scan; every tracked file when empty and no list is given.</summary>
    public List<string> Paths { get; } = [];
}
