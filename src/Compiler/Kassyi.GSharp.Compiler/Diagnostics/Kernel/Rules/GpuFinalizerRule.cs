namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a finalizer (<c>~T()</c>) that carries <c>[GpuKernel]</c> or <c>[GpuDeviceFunction]</c> (GSH0003): the GC
/// runs a finalizer on the host, and the generator transpiles only methods, so the attribute would have no effect.
/// </summary>
/// <remarks>
/// A finalizer of a class that contains kernels, without the attribute, is host code and is not reported. Device code
/// releases a resource with <c>using</c> and <c>Dispose</c> instead (UsingTranspiler).
/// </remarks>
internal sealed class GpuFinalizerRule : ISyntaxValidationRule<DestructorDeclarationSyntax>
{
    public static readonly GpuFinalizerRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private GpuFinalizerRule()
    {
    }

    // Reported at `~Name`, the part of the declaration that makes it a finalizer
    public void Analyze(DestructorDeclarationSyntax node, ref ValidationContext context)
    {
        var span = TextSpan.FromBounds(node.TildeToken.SpanStart, node.Identifier.Span.End);
        context.ReportDiagnostic(
            GSharpDiagnostics.UnsupportedSyntax,
            Location.Create(node.SyntaxTree, span),
            node.Kind().ToString());
    }
}
