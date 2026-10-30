namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>The constructs that the scan of a kernel or suspending device function checks node by node (<see cref="KernelBarrierValidator"/>).</summary>
internal static class KernelConstructChecks
{
    /// <summary>Validates the method header: the async modifier (GSH0046) and, for a device function, the iterator shape.</summary>
    internal static void ValidateKernelHeader(KernelBarrierScan scan)
    {
        // GSH0046: async keyword on a kernel or a device function
        if (scan.Method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.AsyncYieldInKernelProhibited,
                scan.Method.Identifier.GetLocation());
        }

        if (!scan.IsKernel)
        {
            DeviceIteratorChecks.ValidateDeviceIteratorHeader(scan);
        }
    }

    /// <summary>A local function or lambda that is an iterator (GSH0070): only methods can be device iterators.</summary>
    internal static void CheckIteratorFunction(KernelBarrierScan scan, SyntaxNode function)
    {
        switch (function)
        {
            case LocalFunctionStatementSyntax localFunction when IteratorTypeResolver.ContainsDirectYield(localFunction.Body):
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorShapeNotSupported,
                    localFunction.Identifier.GetLocation(),
                    localFunction.Identifier.ValueText,
                    "local function iterators are not supported");
                break;
            case AnonymousFunctionExpressionSyntax lambda when IteratorTypeResolver.ContainsDirectYield(lambda.Body):
                scan.Context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorShapeNotSupported,
                    lambda.GetLocation(),
                    "<lambda>",
                    "anonymous function iterators are not supported");
                break;
        }
    }

    /// <summary>
    /// <c>await</c> and, in a kernel, <c>yield</c> (GSH0046), <c>lock</c> (GSH0047) and <c>try</c> (GSH0038). A
    /// <c>yield</c> inside a lambda or local function is that function's (<see cref="CheckIteratorFunction"/>); in a
    /// device function it makes the function an iterator.
    /// </summary>
    internal static void CheckProhibitedConstruct(KernelBarrierScan scan, SyntaxNode node)
    {
        var (descriptor, location) = node switch
        {
            AwaitExpressionSyntax => (GSharpDiagnostics.AsyncYieldInKernelProhibited, node.GetLocation()),
            YieldStatementSyntax when scan.IsKernel && !DeviceIteratorChecks.IsInsideLocalOrAnonymousFunction(scan, node) =>
                (GSharpDiagnostics.AsyncYieldInKernelProhibited, node.GetLocation()),
            LockStatementSyntax lockStatement =>
                (GSharpDiagnostics.LockStatementInKernelProhibited, lockStatement.LockKeyword.GetLocation()),
            TryStatementSyntax tryStatement =>
                (GSharpDiagnostics.ExceptionHandlingInKernelProhibited, tryStatement.TryKeyword.GetLocation()),
            _ => ((DiagnosticDescriptor?)null, (Location?)null)
        };

        if (descriptor != null)
        {
            scan.Context.ReportDiagnostic(descriptor, location);
        }
    }

    /// <summary>A <c>SharedMemory&lt;T&gt;</c> declared in the body of a device iterator (GSH0072).</summary>
    internal static void CheckIteratorSharedMemory(KernelBarrierScan scan, LocalDeclarationStatementSyntax declaration)
    {
        if (scan.IsDeviceIterator &&
            !DeviceIteratorChecks.IsInsideLocalOrAnonymousFunction(scan, declaration) &&
            scan.SemanticModel.GetTypeSymbol(declaration.Declaration.Type).IsSharedMemoryType())
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorBodyConstructNotSupported,
                declaration.Declaration.Type.GetLocation(),
                "SharedMemory<T>");
        }
    }

    /// <summary>
    /// Classifies a call: a point that splits the state machine (a barrier, a warp shuffle, a grid sync, a call that
    /// suspends) is not allowed in a device iterator or the body of a <c>foreach</c> over one (GSH0072); a block barrier
    /// and a call that suspends are collected as barriers and their ancestors checked.
    /// </summary>
    internal static void InspectInvocation(KernelBarrierScan scan, InvocationExpressionSyntax invocation)
    {
        var callee = scan.SemanticModel.GetSymbol(invocation) as IMethodSymbol;
        // Barriers and warp shuffles split the state machine, however they are spelled (#503)
        var intrinsic = callee != null ? SyncIntrinsicClassifier.Classify(callee) : default;
        var isSyncThreads = intrinsic.Kind == SyncIntrinsicKind.BlockBarrier;
        var suspendingCallee = !isSyncThreads &&
                               callee != null &&
                               SuspensionAnalysis.IsSuspendingFunction(callee, scan.SemanticModel.Compilation)
            ? callee
            : null;
        var splits = intrinsic.SplitsStateMachine || suspendingCallee != null;
        scan.HasSplitPoint |= splits;
        if (splits && (scan.IsDeviceIterator || DeviceIteratorChecks.IsInsideIteratorForEachBody(scan, invocation)))
        {
            scan.Context.ReportDiagnostic(
                GSharpDiagnostics.IteratorBodyConstructNotSupported,
                invocation.GetLocation(),
                IteratorTypeResolver.FormatSyntax(invocation));
            return;
        }

        if (isSyncThreads)
        {
            scan.Barriers.Add(invocation);
            scan.SyncPoints.Add((invocation, SyncScope.Block, IsCall: false));
            BarrierDivergenceChecks.InspectBarrierAncestors(scan, invocation);
        }
        else if (suspendingCallee != null)
        {
            scan.Barriers.Add(invocation);
            var compilation = scan.SemanticModel.Compilation;
            if (SuspensionAnalysis.ReachesBlockBarrier(suspendingCallee, compilation))
            {
                scan.SyncPoints.Add(
                    (invocation, SuspensionAnalysis.GetBarrierScope(suspendingCallee, compilation), IsCall: true)
                );
            }

            BarrierDivergenceChecks.InspectBarrierAncestors(scan, invocation);
        }
        else if (intrinsic.Kind == SyncIntrinsicKind.GridBarrier)
        {
            // B2: a grid barrier written directly in a branch
            scan.SyncPoints.Add((invocation, SyncScope.Grid, IsCall: false));
        }
    }

    /// <summary>Checks if a variable declarator is a ref struct, local pointer, or iterator local that needs cross-barrier tracking.</summary>
    internal static void InspectLocalVariableDeclarator(KernelBarrierScan scan, VariableDeclaratorSyntax varDecl)
    {
        if (scan.SemanticModel.GetDeclaredSymbol<ILocalSymbol>(varDecl) is not { } localSym)
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
            scan.TrackedLocals.Add(new TrackedLocalInfo(varDecl, localSym, isRefLikeOrRef, isLocalPointer, isIteratorLocal));
        }
    }
}
