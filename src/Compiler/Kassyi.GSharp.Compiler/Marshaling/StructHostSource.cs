namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>The native shadow struct and the marshaling methods of one user struct, as the wrapper declares them.</summary>
/// <param name="AotStructPath">The <c>AotContexts</c> path, which identifies the struct within a class.</param>
/// <param name="CudaText">The CUDA declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
/// <param name="CpuText">The CPU declaration and the <c>Marshal_</c> / <c>Unmarshal_</c> methods.</param>
/// <param name="Accessibility">
/// The widest accessibility of the <c>AotContexts</c> class under which its members for the struct, all public, name
/// only types at least as accessible as themselves (<see cref="GeneratedMemberAccessibility"/>).
/// </param>
internal readonly record struct StructHostSource(
    string AotStructPath,
    string CudaText,
    string CpuText,
    Accessibility Accessibility)
{
    public string GetText(bool isCuda) => isCuda ? CudaText : CpuText;
}
