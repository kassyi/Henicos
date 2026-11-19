namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>
/// Identifies the native C++ compiler: its executable path, its version string and the environment variables it reads
/// that change what it produces.
/// </summary>
/// <param name="Path">The resolved compiler executable path or command name.</param>
/// <param name="Version">The resolved compiler version string, or <c>"unknown"</c> if unavailable.</param>
/// <param name="Environment">The values of the environment variables the compiler reads besides its arguments
/// (<c>CL</c>, <c>_CL_</c> and <c>INCLUDE</c> for MSVC; <c>CPATH</c>, <c>CPLUS_INCLUDE_PATH</c> and
/// <c>C_INCLUDE_PATH</c> for GCC and Clang) as <c>NAME=value</c> pairs, or empty when the toolchain records none.
/// </param>
public readonly record struct CompilerIdentity(string Path, string Version, string Environment = "");
