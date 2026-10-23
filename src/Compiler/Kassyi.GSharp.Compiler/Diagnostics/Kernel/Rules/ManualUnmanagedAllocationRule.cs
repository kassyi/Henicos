namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Warns about a call to <c>Gpu.Unsafe.Malloc</c> in a kernel or device function (GSH0029).</summary>
internal sealed class ManualUnmanagedAllocationRule : ISyntaxValidationRule<InvocationExpressionSyntax>
{
    public static readonly ManualUnmanagedAllocationRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.ManualUnmanagedAllocation);

    private ManualUnmanagedAllocationRule()
    {
    }

    // Only the allocation is reported; Free is the call that makes the allocation safe
    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetSymbol(node) is IMethodSymbol
            {
                Name: TypeConstants.GpuUnsafe.Malloc,
                ContainingType: { Name: TypeConstants.GpuUnsafe.Type, ContainingType.Name: TypeConstants.GpuType }
            } method &&
            method.ContainingNamespace.IsCoreNamespace())
        {
            context.ReportDiagnostic(GSharpDiagnostics.ManualUnmanagedAllocation, node);
        }
    }
}
