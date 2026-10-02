namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>
/// Roslyn analyzer that enforces GPU kernel barrier synchronization and state machine constraints (GSH0037 - GSH0047).
/// Optimized for minimal AST passes, zero-allocation traversal, and fast-path exits.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class BarrierUsageAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.DivergentBarrierProhibited, // GSH0037
            GSharpDiagnostics.ExceptionHandlingInKernelProhibited, // GSH0038
            GSharpDiagnostics.CrossBarrierRefStructProhibited, // GSH0039
            GSharpDiagnostics.CrossBarrierGotoProhibited, // GSH0040
            GSharpDiagnostics.CrossBarrierLocalPointerProhibited, // GSH0041
            GSharpDiagnostics.ExternalBarrierCallProhibited, // GSH0042
            GSharpDiagnostics.FixedBlockBarrierProhibited, // GSH0043
            GSharpDiagnostics.KernelParameterRefStructProhibited, // GSH0044
            GSharpDiagnostics.RecursiveBarrierProhibited, // GSH0045
            GSharpDiagnostics.AsyncYieldInKernelProhibited, // GSH0046
            GSharpDiagnostics.LockStatementInKernelProhibited // GSH0047
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Pinpoint hook for Gpu.SyncThreads() calls: O(1) detection for external methods (GSH0042)
        // Eliminates the need to traverse full ASTs of thousands of regular non-GPU methods.
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);

        // Comprehensive state machine rules: only invoked for [GpuKernel] entrypoints.
        context.RegisterSyntaxNodeAction(AnalyzeKernelMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (!IsSyncThreadsInvocation(invocation, context.SemanticModel))
        {
            return;
        }

        // Find enclosing method symbol
        var enclosingSymbol = context.ContainingSymbol;
        while (enclosingSymbol != null && enclosingSymbol.Kind != SymbolKind.Method)
        {
            enclosingSymbol = enclosingSymbol.ContainingSymbol;
        }

        if (enclosingSymbol is not IMethodSymbol enclosingMethod)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.ExternalBarrierCallProhibited,
                invocation.GetLocation()));
            return;
        }

        var isGpuMethod = enclosingMethod.HasAttribute(KnownAttributes.GpuKernel) ||
                          enclosingMethod.HasAttribute(KnownAttributes.GpuDeviceFunction);

        if (!isGpuMethod)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.ExternalBarrierCallProhibited,
                invocation.GetLocation()));
        }
    }

    private static void AnalyzeKernelMethod(SyntaxNodeAnalysisContext context)
    {
        var methodDecl = (MethodDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl);
        if (methodSymbol == null || !methodSymbol.HasAttribute(KnownAttributes.GpuKernel))
        {
            return;
        }

        // 1. Signature and Header Validation
        ValidateKernelHeader(context, methodDecl, semanticModel);

        if (methodDecl.Body == null && methodDecl.ExpressionBody == null)
        {
            return;
        }

        // 2. Body Traversal & Immediate Node Validations
        var barriers = ImmutableArray.CreateBuilder<InvocationExpressionSyntax>();
        var gotos = ImmutableArray.CreateBuilder<GotoStatementSyntax>();
        var labels = ImmutableArray.CreateBuilder<LabeledStatementSyntax>();
        var trackedLocals = ImmutableArray.CreateBuilder<TrackedLocalInfo>();
        var localFunctionsWithBarrier = ImmutableArray.CreateBuilder<LocalFunctionStatementSyntax>();

        ScanKernelBody(context, methodDecl, semanticModel, barriers, gotos, labels, trackedLocals,
            localFunctionsWithBarrier);

        // Fast-path: If no barriers exist, cross-barrier constraints are completely skipped
        if (barriers.Count == 0)
        {
            return;
        }

        // 3. Post-Barrier Semantic Constraint Validations
        ValidateRecursiveLocalFunctions(context, semanticModel, localFunctionsWithBarrier);
        ValidateCrossBarrierGotos(context, gotos, labels, barriers);
        ValidateCrossBarrierLocals(context, methodDecl, semanticModel, trackedLocals, barriers);
    }

    /// <summary>Validates kernel entrypoint signature modifiers and parameters (GSH0044, GSH0046).</summary>
    private static void ValidateKernelHeader(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel)
    {
        // GSH0046: async keyword on [GpuKernel]
        foreach (var modifier in methodDecl.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.AsyncKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.AsyncYieldInKernelProhibited,
                    methodDecl.Identifier.GetLocation()));
                break;
            }
        }

        // GSH0044: ref, out, in, and ref struct parameters on [GpuKernel]
        foreach (var param in methodDecl.ParameterList.Parameters)
        {
            var isByRef = false;
            foreach (var mod in param.Modifiers)
            {
                if (mod.IsKind(SyntaxKind.RefKeyword) || mod.IsKind(SyntaxKind.OutKeyword) ||
                    mod.IsKind(SyntaxKind.InKeyword))
                {
                    isByRef = true;
                    break;
                }
            }

            if (isByRef)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.KernelParameterRefStructProhibited,
                    param.GetLocation(),
                    param.Identifier.ValueText));
                continue;
            }

            var paramSymbol = semanticModel.GetDeclaredSymbol(param);
            if (paramSymbol?.Type.IsRefLikeType == true)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.KernelParameterRefStructProhibited,
                    param.GetLocation(),
                    param.Identifier.ValueText));
            }
        }
    }

    /// <summary>Performs a single-pass AST scan to collect barriers, gotos, labels, tracked locals, and report syntax-level violations.</summary>
    private static void ScanKernelBody(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        ImmutableArray<InvocationExpressionSyntax>.Builder barriers,
        ImmutableArray<GotoStatementSyntax>.Builder gotos,
        ImmutableArray<LabeledStatementSyntax>.Builder labels,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals,
        ImmutableArray<LocalFunctionStatementSyntax>.Builder localFunctionsWithBarrier)
    {
        foreach (var node in methodDecl.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.AwaitExpression:
                case SyntaxKind.YieldReturnStatement:
                case SyntaxKind.YieldBreakStatement:
                    context.ReportDiagnostic(Diagnostic.Create(
                        GSharpDiagnostics.AsyncYieldInKernelProhibited,
                        node.GetLocation()));
                    break;

                case SyntaxKind.LockStatement:
                    var lockStmt = (LockStatementSyntax)node;
                    context.ReportDiagnostic(Diagnostic.Create(
                        GSharpDiagnostics.LockStatementInKernelProhibited,
                        lockStmt.LockKeyword.GetLocation()));
                    break;

                case SyntaxKind.TryStatement:
                    var tryStmt = (TryStatementSyntax)node;
                    context.ReportDiagnostic(Diagnostic.Create(
                        GSharpDiagnostics.ExceptionHandlingInKernelProhibited,
                        tryStmt.TryKeyword.GetLocation()));
                    break;

                case SyntaxKind.GotoStatement:
                    gotos.Add((GotoStatementSyntax)node);
                    break;

                case SyntaxKind.LabeledStatement:
                    labels.Add((LabeledStatementSyntax)node);
                    break;

                case SyntaxKind.VariableDeclarator:
                    InspectLocalVariableDeclarator((VariableDeclaratorSyntax)node, semanticModel, trackedLocals);
                    break;

                case SyntaxKind.InvocationExpression:
                    var inv = (InvocationExpressionSyntax)node;
                    if (IsSyncThreadsInvocation(inv, semanticModel))
                    {
                        barriers.Add(inv);
                        InspectBarrierAncestors(context, inv, methodDecl, localFunctionsWithBarrier);
                    }

                    break;
            }
        }
    }

    /// <summary>Checks if a variable declarator is a ref struct or local pointer that needs cross-barrier tracking.</summary>
    private static void InspectLocalVariableDeclarator(
        VariableDeclaratorSyntax varDecl,
        SemanticModel semanticModel,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals)
    {
        if (semanticModel.GetDeclaredSymbol(varDecl) is not ILocalSymbol localSym)
        {
            return;
        }

        var isRefLikeOrRef = (localSym.Type.IsRefLikeType && localSym.Type.Name != "SharedMemory") || localSym.IsRef;
        var isLocalPointer = false;

        if (localSym.Type is IPointerTypeSymbol && varDecl.Initializer != null)
        {
            foreach (var initChild in varDecl.Initializer.DescendantNodes())
            {
                if (initChild is StackAllocArrayCreationExpressionSyntax
                    or ImplicitStackAllocArrayCreationExpressionSyntax)
                {
                    isLocalPointer = true;
                    break;
                }

                if (initChild is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.AddressOfExpression))
                {
                    isLocalPointer = true;
                    break;
                }
            }
        }

        if (isRefLikeOrRef || isLocalPointer)
        {
            trackedLocals.Add(new TrackedLocalInfo(varDecl, localSym, isRefLikeOrRef, isLocalPointer));
        }
    }

    /// <summary>Validates immediate syntactic ancestors of a barrier invocation (GSH0037, GSH0043, GSH0045).</summary>
    private static void InspectBarrierAncestors(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax barrier,
        MethodDeclarationSyntax methodDecl,
        ImmutableArray<LocalFunctionStatementSyntax>.Builder localFunctionsWithBarrier)
    {
        for (var ancestor = barrier.Parent; ancestor != null && ancestor != methodDecl; ancestor = ancestor.Parent)
        {
            // GSH0043: fixed block barrier prohibited
            if (ancestor is FixedStatementSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.FixedBlockBarrierProhibited,
                    barrier.GetLocation()));
            }

            // GSH0037: Divergent barrier in conditional branches
            if (ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchSectionSyntax)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.DivergentBarrierProhibited,
                    barrier.GetLocation()));
            }

            // GSH0045: Queue local function containing barrier for recursion check
            if (ancestor is LocalFunctionStatementSyntax localFunc)
            {
                var alreadyQueued = false;
                foreach (var queued in localFunctionsWithBarrier)
                {
                    if (queued == localFunc)
                    {
                        alreadyQueued = true;
                        break;
                    }
                }

                if (!alreadyQueued)
                {
                    localFunctionsWithBarrier.Add(localFunc);
                }
            }
        }
    }

    /// <summary>Validates that local functions containing barriers are not recursive (GSH0045).</summary>
    private static void ValidateRecursiveLocalFunctions(
        SyntaxNodeAnalysisContext context,
        SemanticModel semanticModel,
        ImmutableArray<LocalFunctionStatementSyntax>.Builder localFunctionsWithBarrier)
    {
        foreach (var localFunc in localFunctionsWithBarrier)
        {
            var localFuncSymbol = semanticModel.GetDeclaredSymbol(localFunc);
            if (localFuncSymbol == null)
            {
                continue;
            }

            var isRecursive = false;
            foreach (var innerNode in localFunc.DescendantNodes())
            {
                if (innerNode is InvocationExpressionSyntax innerInv)
                {
                    var calledSym = semanticModel.GetSymbolInfo(innerInv).Symbol;
                    if (SymbolEqualityComparer.Default.Equals(calledSym, localFuncSymbol))
                    {
                        isRecursive = true;
                        break;
                    }
                }
            }

            if (isRecursive)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.RecursiveBarrierProhibited,
                    localFunc.Identifier.GetLocation(),
                    localFunc.Identifier.ValueText));
            }
        }
    }

    /// <summary>Validates that goto statements do not jump across barriers (GSH0040).</summary>
    private static void ValidateCrossBarrierGotos(
        SyntaxNodeAnalysisContext context,
        ImmutableArray<GotoStatementSyntax>.Builder gotos,
        ImmutableArray<LabeledStatementSyntax>.Builder labels,
        ImmutableArray<InvocationExpressionSyntax>.Builder barriers)
    {
        if (gotos.Count == 0 || labels.Count == 0)
        {
            return;
        }

        var firstBarrierStart = barriers[0].SpanStart;
        var lastBarrierEnd = barriers[barriers.Count - 1].Span.End;

        foreach (var gotoStmt in gotos)
        {
            if (gotoStmt.Expression is not IdentifierNameSyntax gotoId)
            {
                continue;
            }

            var labelName = gotoId.Identifier.ValueText;

            LabeledStatementSyntax? targetLabel = null;
            foreach (var l in labels)
            {
                if (l.Identifier.ValueText == labelName)
                {
                    targetLabel = l;
                    break;
                }
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

            var crossesBarrier = false;
            foreach (var b in barriers)
            {
                if (b.SpanStart > start && b.Span.End < end)
                {
                    crossesBarrier = true;
                    break;
                }
            }

            if (crossesBarrier)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.CrossBarrierGotoProhibited,
                    gotoStmt.GetLocation()));
            }
        }
    }

    /// <summary>Validates that ref structs and local pointers do not survive across barrier boundaries (GSH0039, GSH0041).</summary>
    private static void ValidateCrossBarrierLocals(
        SyntaxNodeAnalysisContext context,
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        ImmutableArray<TrackedLocalInfo>.Builder trackedLocals,
        ImmutableArray<InvocationExpressionSyntax>.Builder barriers)
    {
        if (trackedLocals.Count == 0)
        {
            return;
        }

        foreach (var tracked in trackedLocals)
        {
            var varStart = tracked.Declarator.SpanStart;

            var hasBarrierAfter = false;
            var firstBarrierAfterEnd = 0;
            foreach (var b in barriers)
            {
                if (b.SpanStart <= varStart)
                {
                    continue;
                }

                hasBarrierAfter = true;
                firstBarrierAfterEnd = b.Span.End;
                break;
            }

            if (!hasBarrierAfter)
            {
                continue;
            }

            var scopeNode = tracked.Declarator.Parent?.Parent?.Parent ?? methodDecl;

            foreach (var childNode in scopeNode.DescendantNodes())
            {
                if (childNode is IdentifierNameSyntax id &&
                    id.SpanStart > firstBarrierAfterEnd &&
                    id.Identifier.ValueText == tracked.IdentifierText)
                {
                    var idSym = semanticModel.GetSymbolInfo(id).Symbol;
                    if (!SymbolEqualityComparer.Default.Equals(idSym, tracked.Symbol))
                    {
                        continue;
                    }

                    if (tracked.IsRefLikeOrRef)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            GSharpDiagnostics.CrossBarrierRefStructProhibited,
                            tracked.Declarator.Identifier.GetLocation(),
                            tracked.IdentifierText));
                    }
                    else if (tracked.IsLocalPointer)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            GSharpDiagnostics.CrossBarrierLocalPointerProhibited,
                            tracked.Declarator.Identifier.GetLocation()));
                    }

                    break;
                }
            }
        }
    }

    private static bool IsSyncThreadsInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax ma)
        {
            return false;
        }

        if (ma.Name.Identifier.ValueText != "SyncThreads")
        {
            return false;
        }

        if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol)
        {
            return symbol.ContainingType?.Name == "Gpu";
        }

        if (ma.Expression is IdentifierNameSyntax idExpr)
        {
            return idExpr.Identifier.ValueText == "Gpu";
        }

        return false;
    }

    private readonly struct TrackedLocalInfo(
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
}
