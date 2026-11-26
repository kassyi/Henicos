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
            var type1 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.GpuBigIntExpr");
            var type2 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.Stdlib.GpuMath.Expressions.GpuBigIntExpr");
            var type3 = compilationContext.Compilation.GetTypeByMetadataName(
                "Kassyi.GSharp.GpuBigInt.Expressions.GpuBigIntExpr");

            if (type1 is null && type2 is null && type3 is null)
            {
                return;
            }

            compilationContext.RegisterOperationBlockAction(
                ctx => AnalyzeOperationBlock(ctx, type1, type2, type3));
        });
    }

    private static void AnalyzeOperationBlock(OperationBlockAnalysisContext context, INamedTypeSymbol? t1,
        INamedTypeSymbol? t2, INamedTypeSymbol? t3)
    {
        ImmutableArray<(ILocalSymbol Local, Location Location)>.Builder? declarators = null;
        HashSet<ILocalSymbol>? trackedLocals = null;
        HashSet<ILocalSymbol>? accepted = null;
        Queue<ILocalSymbol>? worklist = null;

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IVariableDeclaratorOperation declarator:
                        if (IsGpuBigIntExpr(declarator.Symbol.Type, t1, t2, t3))
                        {
                            trackedLocals ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                            trackedLocals.Add(declarator.Symbol);

                            if (!declarator.Symbol.IsRef)
                            {
                                declarators ??=
                                    ImmutableArray.CreateBuilder<(ILocalSymbol Local, Location Location)>();
                                declarators.Add((declarator.Symbol, declarator.Syntax.GetLocation()));

                                if (IsUsingResource(declarator))
                                {
                                    accepted ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                                    if (accepted.Add(declarator.Symbol))
                                    {
                                        worklist ??= new Queue<ILocalSymbol>();
                                        worklist.Enqueue(declarator.Symbol);
                                    }
                                }
                            }
                        }

                        break;

                    case IDeclarationExpressionOperation declExpr:
                        if (declExpr.Expression is ILocalReferenceOperation { Local: var outLocal } &&
                            !outLocal.IsRef &&
                            IsGpuBigIntExpr(outLocal.Type, t1, t2, t3))
                        {
                            trackedLocals ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                            if (trackedLocals.Add(outLocal))
                            {
                                declarators ??=
                                    ImmutableArray.CreateBuilder<(ILocalSymbol Local, Location Location)>();
                                declarators.Add((outLocal, declExpr.Syntax.GetLocation()));
                            }
                        }

                        break;

                    case IDeclarationPatternOperation declPattern:
                        if (declPattern.DeclaredSymbol is ILocalSymbol patternLocal &&
                            !patternLocal.IsRef &&
                            IsGpuBigIntExpr(patternLocal.Type, t1, t2, t3))
                        {
                            trackedLocals ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                            trackedLocals.Add(patternLocal);
                        }

                        break;

                    case IRecursivePatternOperation recPattern:
                        if (recPattern.DeclaredSymbol is ILocalSymbol recLocal &&
                            !recLocal.IsRef &&
                            IsGpuBigIntExpr(recLocal.Type, t1, t2, t3))
                        {
                            trackedLocals ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                            trackedLocals.Add(recLocal);
                        }

                        break;

                    case ISimpleAssignmentOperation assignment:
                        if (IsGpuBigIntExpr(assignment.Target.Type, t1, t2, t3) &&
                            assignment.Target is not ILocalReferenceOperation &&
                            assignment.Target is not IPropertyReferenceOperation { Property.Name: "Value" } &&
                            assignment.Target is not IParameterReferenceOperation
                            {
                                Parameter.RefKind: RefKind.Out or RefKind.Ref
                            })
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                                assignment.Syntax.GetLocation(),
                                GetAssignmentTargetName(assignment.Target)));
                        }

                        break;
                }
            }
        }

        if (trackedLocals is null || declarators is null)
        {
            return;
        }

        Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>? dependents = null;

        foreach (var block in context.OperationBlocks)
        {
            foreach (var operation in block.DescendantsAndSelf())
            {
                if (operation is not ILocalReferenceOperation reference ||
                    !trackedLocals.Contains(reference.Local) ||
                    accepted?.Contains(reference.Local) == true)
                {
                    continue;
                }

                if (ClassifyLocalUse(reference, trackedLocals, t1, t2, t3, ref dependents))
                {
                    accepted ??= new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                    if (accepted.Add(reference.Local))
                    {
                        worklist ??= new Queue<ILocalSymbol>();
                        worklist.Enqueue(reference.Local);
                    }
                }
            }
        }

        // Acceptance on every path is not required: a local accepted on one path is accepted. The rule exists for the
        // obvious discard, and a full flow analysis would report correct code (semantics charter: a form the analysis
        // can only suspect is not an error).
        if (accepted is not null && worklist is not null)
        {
            PropagateAccepted(accepted, worklist, dependents);
        }

        foreach (var (local, location) in declarators)
        {
            if (accepted is null || !accepted.Contains(local))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.GpuBigIntExprDirectAssignment,
                    location,
                    local.Name));
            }
        }
    }

    private static void PropagateAccepted(HashSet<ILocalSymbol> accepted, Queue<ILocalSymbol> worklist,
        Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>? dependents)
    {
        if (dependents is null)
        {
            return;
        }

        while (worklist.Count > 0)
        {
            var current = worklist.Dequeue();
            if (!dependents.TryGetValue(current, out var sources))
            {
                continue;
            }

            foreach (var source in sources)
            {
                if (accepted.Add(source))
                {
                    worklist.Enqueue(source);
                }
            }
        }
    }

    private static bool ClassifyLocalUse(
        ILocalReferenceOperation reference,
        HashSet<ILocalSymbol> trackedLocals,
        INamedTypeSymbol? t1,
        INamedTypeSymbol? t2,
        INamedTypeSymbol? t3,
        ref Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>? dependents)
    {
        if (reference.Parent is ISimpleAssignmentOperation assignTarget && ReferenceEquals(assignTarget.Target, reference))
        {
            return false;
        }

        if (reference.Parent is IDeclarationExpressionOperation)
        {
            return false;
        }

        IOperation current = reference;
        while (current.Parent is { } parent)
        {
            switch (parent)
            {
                case IConversionOperation:
                    current = parent;
                    continue;

                case IConditionalOperation cond
                    when ReferenceEquals(cond.WhenTrue, current) || ReferenceEquals(cond.WhenFalse, current):
                    current = cond;
                    continue;

                case ISwitchExpressionArmOperation arm
                    when ReferenceEquals(arm.Value, current) && arm.Parent is ISwitchExpressionOperation sw:
                    current = sw;
                    continue;

                case ICoalesceOperation coalesce
                    when ReferenceEquals(coalesce.Value, current) || ReferenceEquals(coalesce.WhenNull, current):
                    current = coalesce;
                    continue;

                case IPropertyReferenceOperation nullableValue
                    when ReferenceEquals(nullableValue.Instance, current) &&
                         nullableValue.Property is
                         {
                             Name: "Value",
                             ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                         }:
                    current = nullableValue;
                    continue;

                case IInvocationOperation getValueOrDefault
                    when ReferenceEquals(getValueOrDefault.Instance, current) &&
                         getValueOrDefault.TargetMethod is
                         {
                             Name: "GetValueOrDefault",
                             ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T
                         }:
                    current = getValueOrDefault;
                    continue;

                case IBinaryOperation binary
                    when IsGpuBigIntExprBinaryOperand(binary, current, t1, t2, t3):
                    current = binary;
                    continue;

                case IUnaryOperation unary
                    when unary.OperatorMethod is { Parameters.Length: 1 } unaryOp &&
                         ReferenceEquals(unary.Operand, current) &&
                         IsGpuBigIntExpr(unaryOp.Parameters[0].Type, t1, t2, t3):
                    current = unary;
                    continue;

                case IInvocationOperation fitInv
                    when ReferenceEquals(fitInv.Instance, current) &&
                         fitInv.TargetMethod.Name == "Fit" &&
                         IsGpuBigIntExpr(fitInv.TargetMethod.ReturnType, t1, t2, t3):
                    current = fitInv;
                    continue;

                case IInvocationOperation inv when ReferenceEquals(inv.Instance, current):
                    return IsConsumingMethodName(inv.TargetMethod.Name);

                case IConditionalAccessOperation condAccess when ReferenceEquals(condAccess.Operation, current):
                    if (IsConsumingConditionalAccess(condAccess.WhenNotNull, t1, t2, t3))
                    {
                        return true;
                    }

                    if (condAccess.WhenNotNull is IPropertyReferenceOperation { Property.Name: "Node" } condNode &&
                        ReachesConditionalInstance(condNode.Instance, t1, t2, t3) &&
                        IsNodeReferenceConsumed(condAccess))
                    {
                        return true;
                    }

                    if (IsGpuBigIntExpr(condAccess.Type, t1, t2, t3))
                    {
                        current = condAccess;
                        continue;
                    }

                    return false;

                case IUsingOperation usingOp when ReferenceEquals(usingOp.Resources, current):
                    return true;

                case IPropertyReferenceOperation { Property.Name: "Node" } nodeProp
                    when ReferenceEquals(nodeProp.Instance, current):
                    return IsNodeReferenceConsumed(nodeProp);

                case IArgumentOperation arg when ReferenceEquals(arg.Value, current):
                    return arg.Parameter is { RefKind: not RefKind.Out } param &&
                           IsGpuBigIntExpr(param.Type, t1, t2, t3);

                case IReturnOperation ret when ReferenceEquals(ret.ReturnedValue, current):
                    return true;

                case ISimpleAssignmentOperation assign when ReferenceEquals(assign.Value, current):
                    if (assign.Target is IPropertyReferenceOperation { Property.Name: "Value" })
                    {
                        return true;
                    }

                    if (assign.Target is IParameterReferenceOperation { Parameter.RefKind: RefKind.Out or RefKind.Ref })
                    {
                        return true;
                    }

                    if (assign.Target is ILocalReferenceOperation { Local: var targetLocal } &&
                        trackedLocals.Contains(targetLocal))
                    {
                        AddDependent(ref dependents, targetLocal, reference.Local);
                    }

                    return false;

                case IVariableInitializerOperation init
                    when ReferenceEquals(init.Value, current) &&
                         init.Parent is IVariableDeclaratorOperation targetDecl:
                    if (IsUsingResource(targetDecl))
                    {
                        return true;
                    }

                    if (trackedLocals.Contains(targetDecl.Symbol))
                    {
                        AddDependent(ref dependents, targetDecl.Symbol, reference.Local);
                    }

                    return false;

                case IIsPatternOperation isPattern when ReferenceEquals(isPattern.Value, current):
                    AddPatternDependents(isPattern.Pattern, reference.Local, trackedLocals, ref dependents);
                    return false;

                case ISwitchExpressionOperation switchExpr when ReferenceEquals(switchExpr.Value, current):
                    foreach (var arm in switchExpr.Arms)
                    {
                        AddPatternDependents(arm.Pattern, reference.Local, trackedLocals, ref dependents);
                    }

                    return false;

                case ISwitchOperation switchStmt when ReferenceEquals(switchStmt.Value, current):
                    foreach (var switchCase in switchStmt.Cases)
                    {
                        foreach (var clause in switchCase.Clauses)
                        {
                            if (clause is IPatternCaseClauseOperation patternClause)
                            {
                                AddPatternDependents(patternClause.Pattern, reference.Local, trackedLocals,
                                    ref dependents);
                            }
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }

        return false;
    }

    private static void AddPatternDependents(
        IPatternOperation pattern,
        ILocalSymbol sourceLocal,
        HashSet<ILocalSymbol> trackedLocals,
        ref Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>? dependents)
    {
        switch (pattern)
        {
            case IDeclarationPatternOperation { DeclaredSymbol: ILocalSymbol declLocal }
                when trackedLocals.Contains(declLocal):
                AddDependent(ref dependents, declLocal, sourceLocal);
                break;

            case IRecursivePatternOperation { DeclaredSymbol: ILocalSymbol recLocal }
                when trackedLocals.Contains(recLocal):
                AddDependent(ref dependents, recLocal, sourceLocal);
                break;

            case IBinaryPatternOperation binaryPattern:
                AddPatternDependents(binaryPattern.LeftPattern, sourceLocal, trackedLocals, ref dependents);
                AddPatternDependents(binaryPattern.RightPattern, sourceLocal, trackedLocals, ref dependents);
                break;
        }
    }

    private static void AddDependent(
        ref Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>? dependents,
        ILocalSymbol targetLocal,
        ILocalSymbol sourceLocal)
    {
        dependents ??=
            new Dictionary<ILocalSymbol, ImmutableArray<ILocalSymbol>.Builder>(SymbolEqualityComparer.Default);
        if (!dependents.TryGetValue(targetLocal, out var sources))
        {
            sources = ImmutableArray.CreateBuilder<ILocalSymbol>();
            dependents.Add(targetLocal, sources);
        }

        sources.Add(sourceLocal);
    }

    private static bool IsGpuBigIntExprBinaryOperand(IBinaryOperation binary, IOperation operand,
        INamedTypeSymbol? t1, INamedTypeSymbol? t2, INamedTypeSymbol? t3)
    {
        if (binary.OperatorMethod is not { Parameters.Length: 2 } binOp)
        {
            return false;
        }

        return (ReferenceEquals(binary.LeftOperand, operand) && IsGpuBigIntExpr(binOp.Parameters[0].Type, t1, t2, t3)) ||
               (ReferenceEquals(binary.RightOperand, operand) && IsGpuBigIntExpr(binOp.Parameters[1].Type, t1, t2, t3));
    }

    private static bool IsConsumingConditionalAccess(IOperation whenNotNull, INamedTypeSymbol? t1,
        INamedTypeSymbol? t2, INamedTypeSymbol? t3)
    {
        if (whenNotNull is IInvocationOperation inv)
        {
            if (IsConsumingMethodName(inv.TargetMethod.Name) &&
                ReachesConditionalInstance(inv.Instance, t1, t2, t3))
            {
                return true;
            }

            if (IsConsumingNodeMethodName(inv.TargetMethod.Name) &&
                UnwrapConversions(inv.Instance) is IPropertyReferenceOperation { Property.Name: "Node" } nodeProp &&
                ReachesConditionalInstance(nodeProp.Instance, t1, t2, t3))
            {
                return true;
            }
        }

        if (whenNotNull is IConditionalAccessOperation innerCond &&
            UnwrapConversions(innerCond.Operation) is IPropertyReferenceOperation { Property.Name: "Node" } innerNode &&
            ReachesConditionalInstance(innerNode.Instance, t1, t2, t3) &&
            IsConsumingNodeConditionalAccess(innerCond.WhenNotNull))
        {
            return true;
        }

        return false;
    }

    private static bool IsConsumingNodeConditionalAccess(IOperation whenNotNull) =>
        whenNotNull is IInvocationOperation inv &&
        IsConsumingNodeMethodName(inv.TargetMethod.Name) &&
        UnwrapConversions(inv.Instance) is IConditionalAccessInstanceOperation;

    private static IOperation? UnwrapConversions(IOperation? op)
    {
        while (op is IConversionOperation conversion)
        {
            op = conversion.Operand;
        }

        return op;
    }

    private static bool ReachesConditionalInstance(IOperation? op, INamedTypeSymbol? t1, INamedTypeSymbol? t2,
        INamedTypeSymbol? t3)
    {
        while (op is not null)
        {
            switch (op)
            {
                case IConditionalAccessInstanceOperation:
                    return true;
                case IConversionOperation conv:
                    op = conv.Operand;
                    break;
                case IInvocationOperation fitInv
                    when fitInv.TargetMethod.Name == "Fit" &&
                         IsGpuBigIntExpr(fitInv.TargetMethod.ReturnType, t1, t2, t3):
                    op = fitInv.Instance;
                    break;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool IsNodeReferenceConsumed(IOperation nodeOperation)
    {
        var current = nodeOperation;
        while (current.Parent is IConversionOperation conversion)
        {
            current = conversion;
        }

        return current.Parent switch
        {
            IArgumentOperation arg when ReferenceEquals(arg.Value, current) =>
                arg.Parameter is { RefKind: not RefKind.Out } param &&
                param.HasAttribute(KnownAttributes.TakesOwnership),
            IInvocationOperation inv when ReferenceEquals(inv.Instance, current) =>
                IsConsumingNodeMethodName(inv.TargetMethod.Name),
            IConditionalAccessOperation condAccess when ReferenceEquals(condAccess.Operation, current) =>
                IsConsumingNodeConditionalAccess(condAccess.WhenNotNull),
            IVariableInitializerOperation init
                when ReferenceEquals(init.Value, current) &&
                     init.Parent is IVariableDeclaratorOperation targetDecl =>
                IsUsingResource(targetDecl),
            _ => false
        };
    }

    private static bool IsConsumingNodeMethodName(string name) =>
        name is "Evaluate" or "Accept" or "Dispose" or "DisposeAsync";

    private static bool IsConsumingMethodName(string name) =>
        name is "Evaluate" or "CompileToGraph" or "EvaluateWithCudaGraph" or "EvaluateWithCudaGraphAsync"
            or "Dispose" or "DisposeAsync";

    private static bool IsUsingResource(IVariableDeclaratorOperation declarator) =>
        declarator.Parent?.Parent?.Parent is IUsingOperation or IUsingDeclarationOperation;

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

    private static bool IsGpuBigIntExpr(ITypeSymbol? type, INamedTypeSymbol? t1, INamedTypeSymbol? t2,
        INamedTypeSymbol? t3)
    {
        if (type?.UnwrapNullable() is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var originalDef = namedType.OriginalDefinition;
        return originalDef.IsEqualTo(t1) ||
               originalDef.IsEqualTo(t2) ||
               originalDef.IsEqualTo(t3);
    }
}
