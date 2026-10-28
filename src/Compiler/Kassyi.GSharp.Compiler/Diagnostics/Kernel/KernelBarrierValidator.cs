namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Encapsulates per-kernel AST scanning and validation state inside a zero-allocation ref struct.
/// Eliminates parameter bloat across validation steps while ensuring no heap allocations occur during analysis.
/// </summary>
/// <remarks>
/// A call to a device function that reaches a barrier suspends the caller like the barrier itself, so it counts as a
/// barrier for the cross-barrier rules. The divergence rule (GSH0037) counts the block and grid barriers and the calls
/// that reach one, on the control flow graph (<see cref="UniformityAnalysis"/>). The same rules apply to the body of
/// such a device function (<paramref name="isKernel"/> is false), which is lowered into a state machine as well.
/// </remarks>
internal readonly ref struct KernelBarrierValidator(
    SyntaxNodeAnalysisContext context,
    MethodDeclarationSyntax methodDecl,
    SemanticModel semanticModel,
    bool isKernel,
    MethodFlowGraph flow)
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

    // The barriers and barrier-reaching calls that every thread of the block or the grid must reach (GSH0037, GSH0083)
    private readonly ImmutableArray<(InvocationExpressionSyntax Barrier, SyncScope Scope, bool IsCall)>.Builder
        _syncPoints =
            ImmutableArray.CreateBuilder<(InvocationExpressionSyntax Barrier, SyncScope Scope, bool IsCall)>();

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

        // 4. Barriers that only some of the threads reach (GSH0037), on the control flow graph
        ValidateDivergentBarriers();

        // Fast-path: If no barriers exist, the remaining cross-barrier constraints are completely skipped
        if (_barriers.Count == 0)
        {
            return;
        }

        // 5. Post-Barrier Semantic Constraint Validations
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
                        _syncPoints.Add((inv, SyncScope.Block, IsCall: false));
                        InspectBarrierAncestors(inv);
                    }
                    else if (suspendingCallee != null)
                    {
                        _barriers.Add(inv);
                        if (SuspensionAnalysis.ReachesBlockBarrier(suspendingCallee, semanticModel.Compilation))
                        {
                            _syncPoints.Add((inv, SyncScope.Block, IsCall: true));
                        }

                        InspectBarrierAncestors(inv);
                    }
                    else if (intrinsic.Kind == SyncIntrinsicKind.GridBarrier)
                    {
                        // B2: a grid barrier written directly in a branch
                        _syncPoints.Add((inv, SyncScope.Grid, IsCall: false));
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
    /// Validates the syntactic ancestors of a barrier invocation (GSH0043, GSH0045, and GSH0003 for a barrier in the
    /// scope of a <c>using</c>): rules about where the barrier is written, not about the control flow.
    /// </summary>
    /// <param name="barrier">A block barrier, or a call to a device function that reaches a barrier.</param>
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
        if (_trackedLocals.Count == 0 || !_hasSplitPoint.Value || !flow.TryGetGraph(out var graph))
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

    /// <summary>
    /// Reports a barrier, or a call that reaches one, that a thread-variant branch lets only some threads reach while
    /// the others reach another split point (<see cref="BarrierDivergence.Condition"/>): the branch may be in this
    /// method, or in the callee, depending on the arguments or leaving it early (§3.2.4). It is GSH0037 (error) when
    /// the branch certainly diverges (<see cref="BarrierReport.Certain"/>), GSH0083 (warning) otherwise. A barrier that
    /// the others skip only on their way out of the method (<see cref="BarrierDivergence.ExitOnly"/>) is not reported
    /// here.
    /// A barrier written in a body that the CPU dialects do not split (a lambda other than the bodies of
    /// <c>Gpu.ForUnroll</c> and <c>Gpu.SyncGrid(body)</c>, or a local function) is always GSH0037.
    /// </summary>
    private void ValidateDivergentBarriers()
    {
        if (_syncPoints.Count == 0)
        {
            return;
        }

        UniformityAnalysis? uniformity = null;

        // GSH0084 in a kernel: each condition that sends threads out before a barrier, with the first barrier it skips
        var exits = new Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)>();
        foreach (var (barrier, scope, isCall) in _syncPoints)
        {
            if (!isCall && IsInUnsplitBody(barrier))
            {
                context.ReportDiagnostic(GSharpDiagnostics.DivergentBarrierProhibited, barrier.GetLocation(),
                    $"'{semanticModel.GetSymbol(barrier)?.Name}' cannot be called in a lambda or local function other " +
                    "than the body of Gpu.ForUnroll or Gpu.SyncGrid(body): the CPU dialects resume a kernel only in " +
                    "the statements of its body and of those macro bodies, so the barrier would not wait for the " +
                    "other threads there.");
                continue;
            }

            if (uniformity == null && !flow.TryGetUniformity(out uniformity))
            {
                return;
            }

            var report = uniformity.GetBarrierReport(barrier, scope);
            if (isCall && report.Divergence != BarrierDivergence.Condition)
            {
                var callSite = uniformity.GetCallSiteReport(barrier);
                if (callSite.Divergence == BarrierDivergence.Condition || report.Divergence == BarrierDivergence.None)
                {
                    report = callSite;
                }
            }

            if (report.Divergence != BarrierDivergence.Condition)
            {
                if (isKernel)
                {
                    CollectExits(uniformity, barrier, scope, isCall, exits);
                }

                continue;
            }

            if (report.Certain)
            {
                context.ReportDiagnostic(GSharpDiagnostics.DivergentBarrierProhibited, barrier.GetLocation(),
                    DivergentBarrierMessage);
            }
            else
            {
                context.ReportDiagnostic(GSharpDiagnostics.LaunchDependentDivergentBarrier, barrier.GetLocation(),
                    semanticModel.GetSymbol(barrier)?.Name ?? "", scope == SyncScope.Grid ? "grid" : "block");
            }
        }

        ReportExits(exits);
    }

    // An exit-only barrier of a kernel: its exit conditions, or the call whose callee the threads leave early
    private void CollectExits(UniformityAnalysis uniformity, InvocationExpressionSyntax barrier, SyncScope scope,
        bool isCall, Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)> exits)
    {
        foreach (var branch in uniformity.GetExitBranches(barrier, scope))
        {
            // A case of a switch statement compares with the case value; the switch expression is what diverges
            var condition = branch.FirstAncestorOrSelf<SwitchLabelSyntax>() is
                { Parent: SwitchSectionSyntax { Parent: SwitchStatementSyntax switchStatement } }
                ? switchStatement.Expression
                : branch;
            if (!exits.TryGetValue(condition, out var first) || barrier.SpanStart < first.Barrier.SpanStart)
            {
                exits[condition] = (barrier, scope);
            }
        }

        if (isCall && uniformity.IsExitOnlyCall(barrier))
        {
            var callee = semanticModel.GetSymbol(barrier)?.Name ?? "";
            context.ReportDiagnostic(GSharpDiagnostics.BarrierAfterDivergentExit, barrier.GetLocation(),
                $"Some threads leave '{callee}' early", $"a barrier inside '{callee}'", "block");
        }
    }

    // GSH0084 at each exit condition, with the first barrier it skips as the additional location
    private void ReportExits(Dictionary<SyntaxNode, (InvocationExpressionSyntax Barrier, SyncScope Scope)> exits)
    {
        foreach (var pair in exits)
        {
            var (barrier, scope) = pair.Value;
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.BarrierAfterDivergentExit,
                pair.Key.GetLocation(),
                [barrier.GetLocation()],
                "Where this condition holds, a thread leaves the kernel",
                $"'{semanticModel.GetSymbol(barrier)?.Name}'",
                scope == SyncScope.Grid ? "grid" : "block"));
        }
    }

    private const string DivergentBarrierMessage =
        "Gpu.SyncThreads(), or a call to a device function that reaches it, cannot be placed inside a conditional " +
        "branch or an operand that only some threads evaluate ('&&', '||', '??', '?:'). This causes undefined " +
        "behavior on the GPU and silently reorders shared memory accesses on the CPU emulator.";

    // [WHY] The state machine of the CPU dialects splits only the statements of the method and the inlined macro
    // bodies; a barrier in any other lambda or in a local function cannot be resumed (decision 1.5)
    private bool IsInUnsplitBody(InvocationExpressionSyntax barrier)
    {
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            if (ancestor is LocalFunctionStatementSyntax ||
                (ancestor is AnonymousFunctionExpressionSyntax &&
                 !BarrierUsageAnalyzer.IsInlinedMacroBody(ancestor, semanticModel)))
            {
                return true;
            }
        }

        return false;
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
