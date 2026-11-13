namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Platform-specific library probing candidates. Each entry may be a bare file name, a path relative to
/// the application base directory, or an absolute path, and may contain <c>%VARIABLE%</c> references.
/// </summary>
internal sealed class NativeLibraryNames
{
    public ImmutableArray<string> Windows { get; set; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> Linux { get; set; } = ImmutableArray<string>.Empty;

    public ImmutableArray<string> OSX { get; set; } = ImmutableArray<string>.Empty;

    public bool IsEmpty => Windows.IsEmpty && Linux.IsEmpty && OSX.IsEmpty;
}
