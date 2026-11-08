namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// An input file of a generator mode that cannot be turned into source. <see cref="Program"/> prints
/// <see cref="Exception.Message"/> to standard error and exits with a non-zero code, so the build stops at the file.
/// </summary>
/// <remarks>
/// The message is in the MSBuild canonical error format (<c>file(line): error CODE: text</c>), which the
/// <c>Exec</c> task reports as a build error at that file and line.
/// </remarks>
public sealed class GeneratorInputException : Exception
{
    /// <summary>Creates an error for <paramref name="file"/>.</summary>
    /// <param name="file">The input file the error refers to.</param>
    /// <param name="line">The 1-based line, or <c>0</c> when the error concerns the whole file.</param>
    /// <param name="message">What is wrong and, where it helps, how to fix it.</param>
    /// <param name="code">A diagnostic ID shown after <c>error</c>, or <c>null</c> for none.</param>
    public GeneratorInputException(string file, int line, string message, string? code = null)
        : base(Format(file, line, message, code))
    {
        File = file;
        Line = line;
    }

    /// <summary>Gets the input file the error refers to.</summary>
    public string File { get; }

    /// <summary>Gets the 1-based line, or <c>0</c> when the error concerns the whole file.</summary>
    public int Line { get; }

    private static string Format(string file, int line, string message, string? code)
    {
        var location = line > 0 ? $"{file}({line})" : file;
        return code is null ? $"{location}: error : {message}" : $"{location}: error {code}: {message}";
    }
}
