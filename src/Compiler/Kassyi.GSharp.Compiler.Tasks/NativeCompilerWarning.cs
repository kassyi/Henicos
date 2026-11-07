namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>One warning that nvcc, ptxas or the host C++ compiler printed, split into its parts.</summary>
internal readonly record struct NativeCompilerWarning(
    string? File,
    int Line,
    int Column,
    string? Code,
    string Message,
    string RawLine);
