using Kassyi.GSharp.Compiler.Extensions;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>
/// Encapsulates per-kernel AST scanning and validation state inside a zero-allocation ref struct.
/// Eliminates parameter bloat across validation steps while ensuring no heap allocations occur during analysis.
/// </summary>
internal readonly ref struct KernelBarrierValidator(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax methodDecl,
    SemanticModel semanticModel)
{
    private readonly ImmutableArray<InvocationExpressionSyntax>.Builder _barriers =
        ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();

    private readonly ImmutableArray<GotoStatementSyntax>.Builder _gotos =
        ImmutableArray.CreateBuilder<GotoStatementSyntax>();

    private readonly ImmutableArray<LabeledStatementSyntax>.Builder _labels =
        ImmutableArray.CreateBuilder<LabeledStatementSyntax>();

    private readonly ImmutableArray<TrackedLocalInfo>.Builder _trackedLocals =
        ImmutableArray.CreateBuilder<TrackedLocalInfo>();

    private readonly ImmutableArray<LocalFunctionStatementSyntax>.Builder _localFunctionsWithBarrier =
        ImmutableArray.CreateBuilder<LocalFunctionStatementSyntax>();

    public void Analyze()
    {
        // 1. Signature and Header Validation
        ValidateKernelHeader();

        if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
        {
            return;
        }

        // 2. Body Traversal & Immediate Node Validations
        ScanKernelBody();

        // Fast-path: If no barriers exist, cross-barrier constraints are completely skipped
        if (_barriers.Count == 0)
        {
            return;
        }

        // 3. Post-Barrier Semantic Constraint Validations
        ValidateRecursiveLocalFunctions();
        ValidateCrossBarrierGotos();
        ValidateCrossBarrierLocals();
    }

    /// <summary>Validates kernel entrypoint signature modifiers and parameters (GSH0044, GSH0046).</summary>
    private void ValidateKernelHeader()
    {
        // GSH0046: async keyword on [GpuKernel]
        foreach (var modifier in methodDecl.Modifiers)
        {
            if (!modifier.IsKind(SyntaxKind.AsyncKeyword))
            {
                continue;
            }

            context.ReportDiagnostic(
                GSharpDiagnostics.AsyncYieldInKernelProhibited,
                methodDecl.Identifier.GetLocation());
            break;
        }

        // GSH0044: ref, out, in, and ref struct parameters on [GpuKernel]
        foreach (var param in methodDecl.ParameterList.Parameters)
        {
            var isByRef = false;
            foreach (var mod in param.Modifiers)
            {
                if (!mod.IsKind(SyntaxKind.RefKeyword, SyntaxKind.OutKeyword, SyntaxKind.InKeyword))
                {
                    continue;
                }

                isByRef = true;
                break;
            }

            if (isByRef)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelParameterRefStructProhibited,
                    param.GetLocation(),
                    param.Identifier.ValueText);
                continue;
            }

            var paramSymbol = semanticModel.GetDeclaredSymbol<IParameterSymbol>(param);
            if (paramSymbol?.Type.IsRefLikeType == true)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.KernelParameterRefStructProhibited,
                    param.GetLocation(),
                    param.Identifier.ValueText);
            }
        }
    }

    /// <summary>Performs a single-pass AST scan to collect barriers, gotos, labels, tracked locals, and report syntax-level violations.</summary>
    private void ScanKernelBody()
    {
        foreach (var node in methodDecl.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.AwaitExpression:
                case SyntaxKind.YieldReturnStatement:
                case SyntaxKind.YieldBreakStatement:
                    context.ReportDiagnostic(
                        GSharpDiagnostics.AsyncYieldInKernelProhibited,
                        node.GetLocation());
                    break;

                case SyntaxKind.LockStatement:
                    var lockStmt = (LockStatementSyntax)node;
                    context.ReportDiagnostic(
                        GSharpDiagnostics.LockStatementInKernelProhibited,
                        lockStmt.LockKeyword.GetLocation());
                    break;

                case SyntaxKind.TryStatement:
                    var tryStmt = (TryStatementSyntax)node;
                    context.ReportDiagnostic(
                        GSharpDiagnostics.ExceptionHandlingInKernelProhibited,
                        tryStmt.TryKeyword.GetLocation());
                    break;

                case SyntaxKind.GotoStatement:
                    _gotos.Add((GotoStatementSyntax)node);
                    break;

                case SyntaxKind.LabeledStatement:
                    _labels.Add((LabeledStatementSyntax)node);
                    break;

                case SyntaxKind.VariableDeclarator:
                    InspectLocalVariableDeclarator((VariableDeclaratorSyntax)node);
                    break;

                case SyntaxKind.InvocationExpression:
                    var inv = (InvocationExpressionSyntax)node;
                    if (BarrierUsageAnalyzer.IsSyncThreadsInvocation(inv, semanticModel))
                    {
                        _barriers.Add(inv);
                        InspectBarrierAncestors(inv);
                    }

                    break;
            }
        }
    }

    /// <summary>Checks if a variable declarator is a ref struct or local pointer that needs cross-barrier tracking.</summary>
    private void InspectLocalVariableDeclarator(VariableDeclaratorSyntax varDecl)
    {
        if (semanticModel.GetDeclaredSymbol<ILocalSymbol>(varDecl) is not { } localSym)
        {
            return;
        }

        var isRefLikeOrRef = (localSym.Type.IsRefLikeType && !localSym.Type.IsSharedMemoryType()) || localSym.IsRef;
        var isLocalPointer = false;

        if (localSym.Type is IPointerTypeSymbol && varDecl.Initializer != null)
        {
            foreach (var initChild in varDecl.Initializer.DescendantNodes())
            {
                if (initChild is not (StackAllocArrayCreationExpressionSyntax
                        or ImplicitStackAllocArrayCreationExpressionSyntax) &&
                    (initChild is not PrefixUnaryExpressionSyntax prefix ||
                     !prefix.IsKind(SyntaxKind.AddressOfExpression)))
                {
                    continue;
                }

                isLocalPointer = true;
                break;
            }
        }

        if (isRefLikeOrRef || isLocalPointer)
        {
            _trackedLocals.Add(new TrackedLocalInfo(varDecl, localSym, isRefLikeOrRef, isLocalPointer));
        }
    }

    /// <summary>Validates immediate syntactic ancestors of a barrier invocation (GSH0037, GSH0043, GSH0045).</summary>
    private void InspectBarrierAncestors(InvocationExpressionSyntax barrier)
    {
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            switch (ancestor)
            {
                // GSH0043: fixed block barrier prohibited
                case FixedStatementSyntax:
                    context.ReportDiagnostic(
                        GSharpDiagnostics.FixedBlockBarrierProhibited,
                        barrier.GetLocation());
                    break;
                // GSH0037: Divergent barrier in conditional branches
                case IfStatementSyntax or SwitchStatementSyntax or SwitchSectionSyntax:
                    context.ReportDiagnostic(
                        GSharpDiagnostics.DivergentBarrierProhibited,
                        barrier.GetLocation());
                    break;
                // GSH0045: Queue local function containing barrier for recursion check
                case LocalFunctionStatementSyntax localFunc:
                    {
                        var alreadyQueued = false;
                        foreach (var queued in _localFunctionsWithBarrier)
                        {
                            if (queued != localFunc)
                            {
                                continue;
                            }

                            alreadyQueued = true;
                            break;
                        }

                        if (!alreadyQueued)
                        {
                            _localFunctionsWithBarrier.Add(localFunc);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>Validates that local functions containing barriers are not recursive (GSH0045).</summary>
    private void ValidateRecursiveLocalFunctions()
    {
        foreach (var localFunc in _localFunctionsWithBarrier)
        {
            var localFuncSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(localFunc);
            if (localFuncSymbol == null)
            {
                continue;
            }

            var isRecursive = false;
            foreach (var innerNode in localFunc.DescendantNodes())
            {
                if (innerNode is not InvocationExpressionSyntax innerInv)
                {
                    continue;
                }

                var calledSym = semanticModel.GetSymbol(innerInv);
                if (!calledSym.IsEqualTo(localFuncSymbol))
                {
                    continue;
                }

                isRecursive = true;
                break;
            }

            if (isRecursive)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.RecursiveBarrierProhibited,
                    localFunc.Identifier.GetLocation(),
                    localFunc.Identifier.ValueText);
            }
        }
    }

    /// <summary>Validates that goto statements do not jump across barriers (GSH0040).</summary>
    private void ValidateCrossBarrierGotos()
    {
        if (_gotos.Count == 0 || _labels.Count == 0)
        {
            return;
        }

        var firstBarrierStart = _barriers[0].SpanStart;
        var lastBarrierEnd = _barriers[_barriers.Count - 1].Span.End;

        foreach (var gotoStmt in _gotos)
        {
            if (gotoStmt.Expression is not IdentifierNameSyntax gotoId)
            {
                continue;
            }

            var labelName = gotoId.Identifier.ValueText;

            LabeledStatementSyntax? targetLabel = null;
            foreach (var l in _labels)
            {
                if (l.Identifier.ValueText != labelName)
                {
                    continue;
                }

                targetLabel = l;
                break;
            }

            if (targetLabel == null)
            {
                continue;
            }

            var start = Math.Min(gotoStmt.SpanStart, targetLabel.SpanStart);
            var end = Math.Max(gotoStmt.Span.End, targetLabel.Span.End);

            if (end <= firstBarrierStart || start >= lastBarrierEnd)
            {
                continue;
            }

            var firstBarrier = FindFirstBarrierAfter(start);
            if (firstBarrier != null && firstBarrier.Span.End < end)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.CrossBarrierGotoProhibited,
                    gotoStmt.GetLocation());
            }
        }
    }

    /// <summary>Validates that ref structs and local pointers do not survive across barrier boundaries (GSH0039, GSH0041).</summary>
    private void ValidateCrossBarrierLocals()
    {
        if (_trackedLocals.Count == 0)
        {
            return;
        }

        foreach (var tracked in _trackedLocals)
        {
            var varStart = tracked.Declarator.SpanStart;

            var firstBarrier = FindFirstBarrierAfter(varStart);
            if (firstBarrier == null)
            {
                continue;
            }

            var firstBarrierAfterEnd = firstBarrier.Span.End;

            var scopeNode = tracked.Declarator.Parent?.Parent?.Parent ?? methodDecl;

            foreach (var childNode in scopeNode.DescendantNodes())
            {
                if (childNode is IdentifierNameSyntax id &&
                    id.SpanStart > firstBarrierAfterEnd &&
                    id.Identifier.ValueText == tracked.IdentifierText)
                {
                    var idSym = semanticModel.GetSymbol(id);
                    if (!idSym.IsEqualTo(tracked.Symbol))
                    {
                        continue;
                    }

                    if (tracked.IsRefLikeOrRef)
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.CrossBarrierRefStructProhibited,
                            tracked.Declarator.Identifier.GetLocation(),
                            tracked.IdentifierText);
                    }
                    else if (tracked.IsLocalPointer)
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.CrossBarrierLocalPointerProhibited,
                            tracked.Declarator.Identifier.GetLocation());
                    }

                    break;
                }
            }
        }
    }

    private InvocationExpressionSyntax? FindFirstBarrierAfter(int position)
    {
        var low = 0;
        var high = _barriers.Count - 1;
        var resultIndex = -1;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            if (_barriers[mid].SpanStart > position)
            {
                resultIndex = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        return resultIndex >= 0 ? _barriers[resultIndex] : null;
    }
}

internal readonly struct TrackedLocalInfo(
    VariableDeclaratorSyntax declarator,
    ILocalSymbol symbol,
    bool isRefLikeOrRef,
    bool isLocalPointer)
{
    public readonly VariableDeclaratorSyntax Declarator = declarator;
    public readonly ILocalSymbol Symbol = symbol;
    public readonly string IdentifierText = declarator.Identifier.ValueText;
    public readonly bool IsRefLikeOrRef = isRefLikeOrRef;
    public readonly bool IsLocalPointer = isLocalPointer;
}

