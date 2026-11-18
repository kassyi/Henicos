namespace Kassyi.GSharp.Compiler.Tasks.Toolchains;

/// <summary>Identifies the native C++ compiler executable path and version string.</summary>
/// <param name="Path">The resolved compiler executable path or command name.</param>
/// <param name="Version">The resolved compiler version string, or <c>"unknown"</c> if unavailable.</param>
public readonly record struct CompilerIdentity(string Path, string Version);
