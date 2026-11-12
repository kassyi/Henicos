using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Reports host code that calls, reads or names a GPU-only member (<see cref="GpuOnlyMembers"/>): a call, a property or
/// indexer access, or a method group (a delegate or a function pointer) (GSH0018).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class HostIntrinsicAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.HostIntrinsicCallError);

    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analyzed too; HostReferences skips only G#'s own output
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics
        );
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(
            AnalyzeReference,
            OperationKind.Invocation,
            OperationKind.MethodReference,
            OperationKind.PropertyReference
        );
    }

    private static void AnalyzeReference(OperationAnalysisContext context)
    {
        var operation = context.Operation;
        var member = GetMember(operation);
        if (!GpuOnlyMembers.IsGpuOnly(member) || !HostReferences.IsHostReference(operation, context.CancellationToken))
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.HostIntrinsicCallError,
            operation.Syntax,
            GpuOnlyMembers.GetDisplayName(member.OriginalDefinition)
        );
    }

    private static ISymbol? GetMember(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => invocation.TargetMethod,
        IMethodReferenceOperation reference => reference.Method,
        IPropertyReferenceOperation property => property.Property,
        _ => null
    };
}
