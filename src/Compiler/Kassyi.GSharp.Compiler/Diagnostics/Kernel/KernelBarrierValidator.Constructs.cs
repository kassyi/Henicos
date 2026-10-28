namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

// The constructs that the scan checks or collects node by node
internal readonly ref partial struct KernelBarrierValidator
{
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

    /// <summary>A local function or lambda that is an iterator (GSH0070): only methods can be device iterators.</summary>
    private void CheckIteratorFunction(SyntaxNode function)
    {
        switch (function)
        {
            case LocalFunctionStatementSyntax localFunction when IteratorTypeResolver.ContainsDirectYield(localFunction.Body):
                context.ReportDiagnostic(
                    GSharpDiagnostics.IteratorShapeNotSupported,
                    localFunction.Identifier.GetLocation(),
                    localFunction.Identifier.ValueText,
                    "local function iterators are not supported");
                break;
            case AnonymousFunctionExpressionSyntax lambda when IteratorTypeResolver.ContainsDirectYield(lambda.Body):
                context.ReportDiagnostic(
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
    private void CheckProhibitedConstruct(SyntaxNode node)
    {
        var (descriptor, location) = node switch
        {
            AwaitExpressionSyntax => (GSharpDiagnostics.AsyncYieldInKernelProhibited, node.GetLocation()),
            YieldStatementSyntax when isKernel && !IsInsideLocalOrAnonymousFunction(node) =>
                (GSharpDiagnostics.AsyncYieldInKernelProhibited, node.GetLocation()),
            LockStatementSyntax lockStatement =>
                (GSharpDiagnostics.LockStatementInKernelProhibited, lockStatement.LockKeyword.GetLocation()),
            TryStatementSyntax tryStatement =>
                (GSharpDiagnostics.ExceptionHandlingInKernelProhibited, tryStatement.TryKeyword.GetLocation()),
            _ => ((DiagnosticDescriptor?)null, (Location?)null)
        };

        if (descriptor != null)
        {
            context.ReportDiagnostic(descriptor, location);
        }
    }

    /// <summary>A <c>SharedMemory&lt;T&gt;</c> declared in the body of a device iterator (GSH0072).</summary>
    private void CheckIteratorSharedMemory(LocalDeclarationStatementSyntax declaration)
    {
        if (_isDeviceIterator &&
            !IsInsideLocalOrAnonymousFunction(declaration) &&
            semanticModel.GetTypeSymbol(declaration.Declaration.Type).IsSharedMemoryType())
        {
            context.ReportDiagnostic(
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
    private void InspectInvocation(InvocationExpressionSyntax invocation)
    {
        var callee = semanticModel.GetSymbol(invocation) as IMethodSymbol;
        // Barriers and warp shuffles split the state machine, however they are spelled (#503)
        var intrinsic = callee != null ? SyncIntrinsicClassifier.Classify(callee) : default;
        var isSyncThreads = intrinsic.Kind == SyncIntrinsicKind.BlockBarrier;
        var suspendingCallee = !isSyncThreads &&
                               callee != null &&
                               SuspensionAnalysis.IsSuspendingFunction(callee, semanticModel.Compilation)
            ? callee
            : null;
        var splits = intrinsic.SplitsStateMachine || suspendingCallee != null;
        _hasSplitPoint.Value |= splits;
        if (splits && (_isDeviceIterator || IsInsideIteratorForEachBody(invocation)))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.IteratorBodyConstructNotSupported,
                invocation.GetLocation(),
                IteratorTypeResolver.FormatSyntax(invocation));
            return;
        }

        if (isSyncThreads)
        {
            _barriers.Add(invocation);
            InspectBarrierAncestors(invocation, isBlockBarrier: true);
        }
        else if (suspendingCallee != null)
        {
            _barriers.Add(invocation);
            InspectBarrierAncestors(invocation,
                SuspensionAnalysis.ReachesBlockBarrier(suspendingCallee, semanticModel.Compilation));
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
}
