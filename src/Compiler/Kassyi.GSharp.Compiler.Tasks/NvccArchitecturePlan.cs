namespace Kassyi.GSharp.Compiler.Tasks;

/// <summary>
/// The <c>-gencode</c> options that the <c>GSharpTargetArchitectures</c> entries stand for, and the entries that are
/// not a form nvcc accepts.
/// </summary>
internal sealed record NvccArchitecturePlan(IReadOnlyList<NvccGencode> Gencodes, IReadOnlyList<string> InvalidEntries);
