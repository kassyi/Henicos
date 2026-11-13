namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>One argument of the kernel call in a launcher's C# execution (<see cref="CSharpExecutionPlan"/>).</summary>
/// <param name="Identifier">The launcher's parameter.</param>
/// <param name="Kind">How the call receives it.</param>
/// <param name="TypeName">The type of the copy that the lambda captures, or the element type of a span.</param>
/// <param name="Local">The local that the lambda captures (<see cref="LauncherLocal.CSharpArgument"/>).</param>
/// <param name="Pinned">
/// The <c>fixed</c> pointer of a span or of a <c>ref</c> / <c>out</c> argument
/// (<see cref="LauncherLocal.CSharpPinned"/>), or null.
/// </param>
/// <param name="Length">The length local of a span (<see cref="LauncherLocal.CSharpLength"/>), or null.</param>
internal readonly record struct CSharpArgument(
    string Identifier,
    CSharpArgumentKind Kind,
    string TypeName,
    string Local,
    string? Pinned = null,
    string? Length = null);
