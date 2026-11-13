namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>The candidates the generated resolver probes for one library name, per platform.</summary>
/// <param name="LibraryName">The name the bindings pass to <c>[DllImport]</c>, which the resolver is asked for.</param>
/// <param name="Windows">The candidates on Windows, then <paramref name="LibraryName"/> unless one names it.</param>
/// <param name="Linux">The candidates on Linux, then <paramref name="LibraryName"/> unless one names it.</param>
/// <param name="OSX">The candidates on macOS, then <paramref name="LibraryName"/> unless one names it.</param>
internal readonly record struct NativeLibraryCandidates(
    string LibraryName,
    EquatableArray<string> Windows,
    EquatableArray<string> Linux,
    EquatableArray<string> OSX);
