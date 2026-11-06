namespace Kassyi.GSharp.Tools.SmellScan;

/// <summary>The configuration file is wrong. The message starts with the file name and the 1-based line.</summary>
internal sealed class SmellConfigException : Exception
{
    /// <summary>Creates the exception for a problem at <paramref name="line"/> of <paramref name="file"/>.</summary>
    public SmellConfigException(string file, int line, string problem)
        : base($"{file}:{line}: {problem}")
    {
        Line = line;
        Problem = problem;
    }

    /// <summary>Gets the 1-based line of the problem.</summary>
    public int Line { get; }

    /// <summary>Gets the problem without the location.</summary>
    public string Problem { get; }
}
