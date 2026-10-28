using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Encapsulates per-kernel AST scanning and validation state inside a zero-allocation ref struct.
/// Eliminates parameter bloat across validation steps while ensuring no heap allocations occur during analysis.
/// </summary>
/// <remarks>
/// A call to a device function that reaches a barrier suspends the caller like the barrier itself, so it counts as a
/// barrier for the cross-barrier rules. The divergence rule (GSH0037) counts only calls that reach a block barrier.
/// The same rules apply to the body of such a device function (<paramref name="isKernel"/> is false), which is lowered
/// into a state machine as well.
/// </remarks>
internal readonly ref struct KernelBarrierValidator(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax methodDecl,
    SemanticModel semanticModel,
    bool isKernel)
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

    private readonly bool _isDeviceIterator = !isKernel && IteratorTypeResolver.ContainsDirectYield(methodDecl.Body);

    // Set by the scan when the body has a call that splits the state machine (a barrier, a warp shuffle, a grid sync, a
    // call that suspends); only then is the control flow graph built for the cross-barrier rules
    private readonly StrongBox<bool> _hasSplitPoint = new();

    // Computed on the first branch around a block barrier
    private readonly ThreadVarianceAnalysis _variance = new(semanticModel, methodDecl);

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

        // 3. Locals live across a point where the state machine splits (GSH0039, GSH0041, GSH0072), on the control flow
        // graph: every split point counts, also a grid sync or a warp shuffle that the barrier list below leaves out
        ValidateCrossBarrierLocals();

        // Fast-path: If no barriers exist, the remaining cross-barrier constraints are completely skipped
        if (_barriers.Count == 0)
        {
            return;
        }

        // 4. Post-Barrier Semantic Constraint Validations
        ValidateRecursiveLocalFunctions();
        ValidateCrossBarrierGotos();
    }

    /// <summary>Validates the method header: the async modifier (GSH0046) and, for a device function, the iterator shape.</summary>
    private void ValidateKernelHeader()
    {
        // GSH0046: async keyword on a kernel or a device function
        if (methodDecl.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.AsyncYieldInKernelProhibited,
                methodDecl.Identifier.GetLocation());
        }

        if (!isKernel)
        {
            ValidateDeviceIteratorHeader();
        }
    }

    /// <summary>Performs a single-pass AST scan to collect barriers, gotos, labels, tracked locals, and report syntax-level violations.</summary>
    private void ScanKernelBody()
    {
        foreach (var node in methodDecl.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.LocalFunctionStatement:
                    var localFunc = (LocalFunctionStatementSyntax)node;
                    if (IteratorTypeResolver.ContainsDirectYield(localFunc.Body))
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.IteratorShapeNotSupported,
                            localFunc.Identifier.GetLocation(),
                            localFunc.Identifier.ValueText,
                            "local function iterators are not supported");
                    }

                    break;

                case SyntaxKind.SimpleLambdaExpression:
                case SyntaxKind.ParenthesizedLambdaExpression:
                case SyntaxKind.AnonymousMethodExpression:
                    var anonFunc = (AnonymousFunctionExpressionSyntax)node;
                    if (IteratorTypeResolver.ContainsDirectYield(anonFunc.Body))
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.IteratorShapeNotSupported,
                            anonFunc.GetLocation(),
                            "<lambda>",
                            "anonymous function iterators are not supported");
                    }

                    break;

                case SyntaxKind.AwaitExpression:
                    context.ReportDiagnostic(
                        GSharpDiagnostics.AsyncYieldInKernelProhibited,
                        node.GetLocation());
                    break;

                case SyntaxKind.YieldReturnStatement:
                case SyntaxKind.YieldBreakStatement:
                    if (IsInsideLocalOrAnonymousFunction(node))
                    {
                        break;
                    }

                    if (isKernel)
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.AsyncYieldInKernelProhibited,
                            node.GetLocation());
                    }

                    break;

                case SyntaxKind.LocalDeclarationStatement:
                    if (_isDeviceIterator && !IsInsideLocalOrAnonymousFunction(node))
                    {
                        var localDecl = (LocalDeclarationStatementSyntax)node;
                        if (semanticModel.GetTypeSymbol(localDecl.Declaration.Type).IsSharedMemoryType())
                        {
                            context.ReportDiagnostic(
                                GSharpDiagnostics.IteratorBodyConstructNotSupported,
                                localDecl.Declaration.Type.GetLocation(),
                                "SharedMemory<T>");
                        }
                    }

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
                    var callee = semanticModel.GetSymbol(inv) as IMethodSymbol;
                    // Barriers and warp shuffles split the state machine, however they are spelled (#503)
                    var intrinsic = callee != null ? SyncIntrinsicClassifier.Classify(callee) : default;
                    var isSyncThreads = intrinsic.Kind == SyncIntrinsicKind.BlockBarrier;
                    var isPhaseBarrier = intrinsic.SplitsStateMachine;
                    IMethodSymbol? suspendingCallee = null;
                    if (!isSyncThreads &&
                        callee != null &&
                        SuspensionAnalysis.IsSuspendingFunction(callee, semanticModel.Compilation))
                    {
                        suspendingCallee = callee;
                    }

                    _hasSplitPoint.Value |= isPhaseBarrier || suspendingCallee != null;
                    if ((isPhaseBarrier || suspendingCallee != null) &&
                        (_isDeviceIterator || IsInsideIteratorForEachBody(inv)))
                    {
                        context.ReportDiagnostic(
                            GSharpDiagnostics.IteratorBodyConstructNotSupported,
                            inv.GetLocation(),
                            IteratorTypeResolver.FormatSyntax(inv));
                        break;
                    }

                    if (isSyncThreads)
                    {
                        _barriers.Add(inv);
                        InspectBarrierAncestors(inv, isBlockBarrier: true);
                    }
                    else if (suspendingCallee != null)
                    {
                        _barriers.Add(inv);
                        InspectBarrierAncestors(inv,
                            SuspensionAnalysis.ReachesBlockBarrier(suspendingCallee, semanticModel.Compilation));
                    }

                    break;
            }
        }
    }

    /// <summary>Checks if a variable declarator is a ref struct, local pointer, or iterator local that needs cross-barrier tracking.</summary>
    private void InspectLocalVariableDeclarator(VariableDeclaratorSyntax varDecl)
    {
        if (semanticModel.GetDeclaredSymbol<ILocalSymbol>(varDecl) is not { } localSym)
        {
            return;
        }

        var isRefLikeOrRef = (localSym.Type.IsRefLikeType && !localSym.Type.IsSharedMemoryType()) || localSym.IsRef;
        var isLocalPointer = localSym.Type is IPointerTypeSymbol &&
            varDecl.Initializer != null &&
            varDecl.Initializer.DescendantNodes()
                .AsStackEnumerable()
                .Any(static initChild =>
                    initChild is StackAllocArrayCreationExpressionSyntax
                        or ImplicitStackAllocArrayCreationExpressionSyntax ||
                    initChild.IsKind(SyntaxKind.AddressOfExpression)
                );
        var isIteratorLocal = IteratorTypeResolver.IsGenericIEnumerable(localSym.Type, out _);

        if (isRefLikeOrRef || isLocalPointer || isIteratorLocal)
        {
            _trackedLocals.Add(new TrackedLocalInfo(varDecl, localSym, isRefLikeOrRef, isLocalPointer, isIteratorLocal));
        }
    }

    /// <summary>
    /// Validates immediate syntactic ancestors of a barrier invocation (GSH0037, GSH0043, GSH0045, and GSH0003 for a
    /// barrier in the scope of a <c>using</c>).
    /// </summary>
    /// <param name="barrier">A block barrier, or a call to a device function that reaches a barrier.</param>
    /// <param name="isBlockBarrier">Whether every thread of the block has to reach <paramref name="barrier"/> (GSH0037).</param>
    private void InspectBarrierAncestors(InvocationExpressionSyntax barrier, bool isBlockBarrier)
    {
        SyntaxNode previous = barrier;
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            var child = previous;
            previous = ancestor;

            // GSH0037: only the threads for which a thread-variant condition holds reach the barrier
            if (isBlockBarrier && GetDivergingCondition(ancestor, child) is { } condition && _variance.IsThreadVariant(condition))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.DivergentBarrierProhibited,
                    barrier.GetLocation());
            }

            switch (ancestor)
            {
                // GSH0043: fixed block barrier prohibited
                case FixedStatementSyntax:
                    context.ReportDiagnostic(
                        GSharpDiagnostics.FixedBlockBarrierProhibited,
                        barrier.GetLocation());
                    break;
                // GSH0003: the state machine would leave the scope of the resource at the barrier and dispose it
                case UsingStatementSyntax:
                case BlockSyntax block when IsInUsingDeclarationScope(block, barrier):
                    context.ReportDiagnostic(
                        GSharpDiagnostics.UnsupportedSyntax,
                        barrier.GetLocation(),
                        GpuCodeConstraints.BarrierInUsingScope);
                    break;
                // GSH0045: Queue local function containing barrier for recursion check
                case LocalFunctionStatementSyntax localFunc:
                    {
                        if (!_localFunctionsWithBarrier.Contains(localFunc))
                        {
                            _localFunctionsWithBarrier.Add(localFunc);
                        }

                        break;
                    }
            }
        }
    }

    /// <summary>
    /// The condition that decides whether <paramref name="child"/> of <paramref name="ancestor"/> runs: a branch, the
    /// body of a loop, or an operand that is evaluated only for some values of another operand. GSH0077 decides with
    /// it which dynamic <c>SharedMemory&lt;T&gt;</c> declarations only some threads reach.
    /// </summary>
    internal static ExpressionSyntax? GetDivergingCondition(SyntaxNode ancestor, SyntaxNode child) => ancestor switch
    {
        IfStatementSyntax ifStatement when child != ifStatement.Condition => ifStatement.Condition,
        SwitchStatementSyntax switchStatement when child != switchStatement.Expression => switchStatement.Expression,
        WhileStatementSyntax whileStatement when child != whileStatement.Condition => whileStatement.Condition,
        DoStatementSyntax doStatement when child != doStatement.Condition => doStatement.Condition,
        ForStatementSyntax { Condition: { } condition } forStatement when child == forStatement.Statement => condition,
        ForEachStatementSyntax forEach when child == forEach.Statement => forEach.Expression,
        ConditionalExpressionSyntax conditional when child != conditional.Condition => conditional.Condition,
        BinaryExpressionSyntax binary when child == binary.Right &&
                                           binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression,
                                               SyntaxKind.CoalesceExpression) => binary.Left,
        _ => null
    };

    /// <summary>
    /// Whether <paramref name="barrier"/> in <paramref name="block"/> is in the scope of a <c>using</c> declaration of the
    /// block: one declared before it, or any one when the block is a nested block, whose statements the lowering moves
    /// into the enclosing block (BranchRewriter, LoopRewriter) so that its scope would end there.
    /// </summary>
    private static bool IsInUsingDeclarationScope(BlockSyntax block, InvocationExpressionSyntax barrier)
    {
        var isNested = block.Parent is BlockSyntax;
        return block.Statements
            .AsStackEnumerable()
            .Any((barrier.SpanStart, isNested), static (statement, state) =>
                UsingTranspiler.IsUsingDeclaration(statement) &&
                (state.isNested || statement.SpanStart < state.SpanStart));
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

            var isRecursive = localFunc.DescendantNodes()
                .AsStackEnumerable()
                .Any(
                    (Model: semanticModel, Target: localFuncSymbol),
                    static (innerNode, s) =>
                        innerNode is InvocationExpressionSyntax innerInv &&
                        s.Model.GetSymbol(innerInv).IsEqualTo(s.Target)
                );

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

            var targetLabel = _labels
                .AsStackEnumerable()
                .FirstOrDefault(labelName, static (l, name) => l.Identifier.ValueText == name);

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

    /// <summary>
    /// Validates that ref structs, local pointers, and iterator locals are not live across a point where the state machine
    /// splits (GSH0039, GSH0041, GSH0072): some path from the split uses them before assigning them again
    /// (<see cref="CrossBarrierLiveness"/>). The state machine cannot keep such a local in the thread's frame.
    /// </summary>
    private void ValidateCrossBarrierLocals()
    {
        if (_trackedLocals.Count == 0 || !_hasSplitPoint.Value || !TryCreateControlFlowGraph(out var graph))
        {
            return;
        }

        var locals = new ILocalSymbol[_trackedLocals.Count];
        for (var i = 0; i < locals.Length; i++)
        {
            locals[i] = _trackedLocals[i].Symbol;
        }

        var liveAcrossSplit = CrossBarrierLiveness.FindLiveAcrossSplit(graph, locals, semanticModel);
        for (var i = 0; i < locals.Length; i++)
        {
            if (!liveAcrossSplit[i])
            {
                continue;
            }

            var tracked = _trackedLocals[i];
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
            else if (tracked.IsIteratorLocal)
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorBodyConstructNotSupported,
                    tracked.Declarator.Identifier.GetLocation(),
                    tracked.IdentifierText);
            }
        }
    }

    // The control flow graph of the body; none while the body does not bind (being edited), so nothing is reported then
    private bool TryCreateControlFlowGraph([NotNullWhen(true)] out ControlFlowGraph? graph)
    {
        graph = null;
        if (semanticModel.GetOperation(methodDecl) is not IMethodBodyOperation body)
        {
            return false;
        }

        foreach (var operation in body.Descendants())
        {
            if (operation is IInvalidOperation)
            {
                return false;
            }
        }

        graph = ControlFlowGraph.Create(body);
        return true;
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

    private void ValidateDeviceIteratorHeader()
    {
        if (!_isDeviceIterator ||
            semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl) is not { } methodSymbol)
        {
            return;
        }

        if (!methodSymbol.IsStatic)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "iterator methods must be static");
            return;
        }

        if (methodSymbol.IsGenericMethod)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "generic iterator methods are not supported");
            return;
        }

        if (!IteratorTypeResolver.IsGenericIEnumerable(methodSymbol.ReturnType, out var elementType))
        {
            var formattedReturnType = IteratorTypeResolver.FormatType(methodSymbol.ReturnType);
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                $"return type must be System.Collections.Generic.IEnumerable<T>, not '{formattedReturnType}'");
            return;
        }

        if (IteratorTypeResolver.TryGetUnsupportedElementTypeReason(elementType, out var reason))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                reason);
            return;
        }

        if (IteratorTypeResolver.IsRecursiveIterator(methodSymbol, semanticModel.Compilation))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorShapeNotSupported,
                methodDecl.Identifier.GetLocation(),
                methodSymbol.Name,
                "recursive iterators are not supported");
        }
    }

    private bool IsInsideLocalOrAnonymousFunction(SyntaxNode node)
    {
        for (var ancestor = node.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInsideIteratorForEachBody(SyntaxNode node)
    {
        SyntaxNode child = node;
        for (var ancestor = node.Parent;
             ancestor != null && ancestor != methodDecl;
             child = ancestor, ancestor = ancestor.Parent)
        {
            if (ancestor is ForEachStatementSyntax forEach &&
                child == forEach.Statement &&
                IteratorTypeResolver.IsIteratorForEachSource(forEach.Expression, semanticModel))
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly struct TrackedLocalInfo(
    VariableDeclaratorSyntax declarator,
    ILocalSymbol symbol,
    bool isRefLikeOrRef,
    bool isLocalPointer,
    bool isIteratorLocal)
{
    public readonly VariableDeclaratorSyntax Declarator = declarator;
    public readonly ILocalSymbol Symbol = symbol;
    public readonly string IdentifierText = declarator.Identifier.ValueText;
    public readonly bool IsRefLikeOrRef = isRefLikeOrRef;
    public readonly bool IsLocalPointer = isLocalPointer;
    public readonly bool IsIteratorLocal = isIteratorLocal;
}
