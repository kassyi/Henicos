using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Roslyn analyzer that reports <see cref="GSharpDiagnostics.GpuBigIntExprDirectAssignment"/> (GSH0005) when a
/// <c>GpuBigIntExpr</c> local is discarded without being evaluated or disposed, or when a <c>GpuBigIntExpr</c> is
/// assigned directly to a non-local target such as a field.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class GpuBigIntExprAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.GpuBigIntExprDirectAssignment);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static compilationContext =>
        {
            if (GpuBigIntExprTypes.TryGet(compilationContext.Compilation, out var types))
            {
                compilationContext.RegisterOperationBlockAction(context => AnalyzeOperationBlock(context, types));
            }
        });
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context, GpuBigIntExprTypes types)
    {
        GpuBigIntExprLocals? locals = null;
        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is ISimpleAssignmentOperation assignment)
                {
                    ReportNonLocalAssignment(context, assignment, types);
                }
                else
                {
                    CollectLocal(operation, types, ref locals);
                }
            }
        }

        if (locals is null)
        {
            return;
        }

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is ILocalReferenceOperation reference &&
                    locals.IsTracked(reference.Local) &&
                    !locals.IsKept(reference.Local) &&
                    GpuBigIntExprUses.KeepsValue(reference, locals))
                {
                    locals.Keep(reference.Local);
                }
            }
        }

        locals.PropagateKept();
        locals.ReportDropped(context);
    }

    // Records a GpuBigIntExpr local that a declarator, an out variable or a pattern declares
    private static void CollectLocal(IOperation operation, GpuBigIntExprTypes types, ref GpuBigIntExprLocals? locals)
    {
        switch (operation)
        {
            // A foreach variable is a copy of an element that the collection owns
            case IVariableDeclaratorOperation { Parent: not IForEachLoopOperation } declarator
                when types.Matches(declarator.Symbol.Type):
                locals ??= new GpuBigIntExprLocals(types);
                locals.Track(declarator.Symbol);
                if (!declarator.Symbol.IsRef)
                {
                    locals.Declare(declarator.Symbol, declarator.Syntax.GetLocation());
                    if (GpuBigIntExprUses.IsUsingResource(declarator))
                    {
                        locals.Keep(declarator.Symbol);
                    }
                }

                break;

            case IDeclarationExpressionOperation
            {
                Expression: ILocalReferenceOperation { Local: var outLocal }
            } declaration
                when !outLocal.IsRef && types.Matches(outLocal.Type):
                locals ??= new GpuBigIntExprLocals(types);
                if (locals.Track(outLocal))
                {
                    locals.Declare(outLocal, declaration.Syntax.GetLocation());
                }

                break;

            case IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol { IsRef: false } patternLocal }
                when types.Matches(patternLocal.Type):
                (locals ??= new GpuBigIntExprLocals(types)).Track(patternLocal);
                break;

            case IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol { IsRef: false } patternLocal }
                when types.Matches(patternLocal.Type):
                (locals ??= new GpuBigIntExprLocals(types)).Track(patternLocal);
                break;
        }
    }

    private static void ReportNonLocalAssignment(OperationBlockAnalysisContext context,
        ISimpleAssignmentOperation assignment, GpuBigIntExprTypes types)
    {
        if (types.Matches(assignment.Target.Type) &&
            assignment.Target is not ILocalReferenceOperation &&
            assignment.Target is not IPropertyReferenceOperation { Property.Name: "Value" } &&
            assignment.Target is not IParameterReferenceOperation { Parameter.RefKind: RefKind.Out or RefKind.Ref })
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                assignment.Syntax.GetLocation(),
                GetAssignmentTargetName(assignment.Target)));
        }
    }

    private static string GetAssignmentTargetName(IOperation target) => target switch
    {
        IFieldReferenceOperation field => field.Field.Name,
        IPropertyReferenceOperation property => property.Property.Name,
        IParameterReferenceOperation parameter => parameter.Parameter.Name,
        IArrayElementReferenceOperation arrayElement => GetAssignmentTargetName(arrayElement.ArrayReference) + "[]",
        IEventReferenceOperation eventRef => eventRef.Event.Name,
        IDiscardOperation => "_",
        _ => target.Type?.Name ?? "GpuBigIntExpr"
    };
}
