namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Reports host code that runs or references a <c>[GpuKernel]</c> method other than through its launcher (GSH0031):
/// a call, a method group in any conversion, a function pointer, and the methods C# calls for the syntax of an
/// operator, a conversion, a property, a <c>foreach</c>, a deconstruction, a pattern or an <c>await</c>
/// (<see cref="OperationCallees"/>).
/// </summary>
/// <remarks>
/// Generated code is analyzed too (a <c>*.g.cs</c> file, an <c>&lt;auto-generated/&gt;</c> header, the output of other
/// generators); what is not host code that runs is left out by <see cref="HostReferences.IsHostReference"/>: G#'s own
/// output, device code (GSH0003 covers a call there), <c>nameof</c>, and a call that C# removes with its arguments.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HostGpuReferenceAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.DirectGpuKernelInvocation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
        );
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeOperation, OperationCallees.Kinds);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        var callee = OperationCallees.FindFirst(
            operation,
            static method => method.HasAttribute(KnownAttributes.GpuKernel)
        );
        if (callee is not { } kernel || !HostReferences.IsHostReference(operation, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(GSharpDiagnostics.DirectGpuKernelInvocation, kernel.Site, kernel.Method.Name);
    }
}
