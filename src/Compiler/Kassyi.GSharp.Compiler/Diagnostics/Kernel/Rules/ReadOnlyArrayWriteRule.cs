using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a write to a <c>[GpuReadOnly]</c> array that the compiler can see (GSH0118). The attribute promises that no
/// thread writes the array while the kernel runs, through any alias, so a write is an error at the write, also through
/// a span or array converted from it and through a method that writes its parameter
/// (<see cref="ReadOnlyArrayWriteAnalysis"/>).
/// </summary>
internal sealed class ReadOnlyArrayWriteRule : ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly ReadOnlyArrayWriteRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.ReadOnlyArrayWritten);

    private ReadOnlyArrayWriteRule()
    {
    }

    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        if (node.GetBodyNode() is not { } body)
        {
            return;
        }

        var analysis = new ReadOnlyArrayWriteAnalysis(context.SemanticModel.Compilation);
        foreach (var write in analysis.Run(body, context.SemanticModel))
        {
            context.ReportDiagnostic(GSharpDiagnostics.ReadOnlyArrayWritten, write.Node, write.Name, write.Reason);
        }
    }
}
