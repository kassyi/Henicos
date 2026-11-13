namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports <c>default</c> of <c>SharedMemory&lt;T&gt;</c> (GSH0003, <see cref="GpuCodeConstraints.SharedMemoryWithoutCount"/>):
/// shared memory has no size until it is declared with an element count. <c>new SharedMemory&lt;T&gt;()</c> needs no
/// rule, since its constructor is obsolete as an error and the C# compiler rejects it.
/// </summary>
internal sealed class SharedMemoryWithoutCountRule :
    ISyntaxValidationRule<LiteralExpressionSyntax>,
    ISyntaxValidationRule<DefaultExpressionSyntax>
{
    public static readonly SharedMemoryWithoutCountRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private SharedMemoryWithoutCountRule()
    {
    }

    public void Analyze(LiteralExpressionSyntax node, ref ValidationContext context)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            Report(node, ref context);
        }
    }

    public void Analyze(DefaultExpressionSyntax node, ref ValidationContext context) => Report(node, ref context);

    // The type of a default literal is the type it converts to
    private static void Report(ExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node).IsSharedMemoryType())
        {
            context.ReportDiagnostic(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.SharedMemoryWithoutCount);
        }
    }
}
