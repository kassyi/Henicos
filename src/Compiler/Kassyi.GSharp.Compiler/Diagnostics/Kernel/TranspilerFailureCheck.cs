namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Runs the transpiler on a kernel or device function as <see cref="GpuKernelGenerator"/> does, for the CUDA dialect on
/// the user's own tree, and turns what it could not emit (<see cref="TranspileFailure"/>) into diagnostics.
/// </summary>
/// <remarks>
/// [WHY] The same approach as the LibraryImport analyzer of dotnet/runtime, which runs the stub generator and keeps only its
/// diagnostics: whether a construct can be emitted is decided once, by the code that emits it. A syntax kind that no rule
/// handles, including one that a newer C# adds, reaches <see cref="RoslynToCudaVisitor.DefaultVisit"/> and is reported as
/// GSH0003 without a list of rejected kinds; a check that depends on the context or the type is decided as the transpiler
/// decides it. The output is dropped.
/// <para>
/// Only the CUDA dialect runs: it transpiles the user's tree, so the plan is never lowered, which keeps the analyzer within
/// the generator's time. A failure that only the C++ dialect's lowered tree has is stated by the generator as
/// <c>#error</c> in the C++ output, and KernelDiagnosticsLeakTests checks that no such failure exists today.
/// </para>
/// </remarks>
internal static class TranspilerFailureCheck
{
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();

    /// <summary>Adds a diagnostic for each failure of the CUDA transpilation that the generator runs for <paramref name="method"/>.</summary>
    /// <remarks>
    /// When the transpiler throws, the failures of this method are dropped and nothing is added for it: the generator runs
    /// the same transpilation, catches the same exception and reports it as an internal error (GSH0048).
    /// </remarks>
    public static void AddFailures(MethodDeclarationSyntax declaration, IMethodSymbol method, SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var reportedBefore = diagnostics.Count;
        try
        {
            var isDeviceIteratorCandidate =
                method.HasAttribute(KnownAttributes.GpuDeviceFunction) &&
                !method.HasAttribute(KnownAttributes.GpuKernel) &&
                IteratorTypeResolver.ContainsDirectYield(declaration.Body);
            var isValidDeviceIterator =
                isDeviceIteratorCandidate &&
                IteratorTypeResolver.IsDeviceIteratorMethod(method) &&
                !IteratorTypeResolver.IsRecursiveIterator(method, semanticModel.Compilation);
            Transpile(
                declaration,
                method,
                semanticModel,
                new FailureSink(diagnostics, declaration, method.HasAttribute(KnownAttributes.GpuKernel),
                    isDeviceIteratorCandidate, isValidDeviceIterator));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // [WHY] Not swallowed: GpuKernelGenerator reports this exception for the same method as GSH0048. Letting it
            // escape would fail the whole analyzer (AD0001) and lose the rules' diagnostics of every other method, and a
            // transpilation that stopped half way has no complete list of failures, so the partial one is withdrawn.
            diagnostics.Count = reportedBefore;
        }
    }

    private static void Transpile(MethodDeclarationSyntax declaration, IMethodSymbol method, SemanticModel semanticModel,
        FailureSink sink)
    {
        if (method.HasAttribute(KnownAttributes.GpuKernel))
        {
            var plan = MethodEmissionPlan.Create(declaration, semanticModel, isKernel: true);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cudaDialect, out _);
        }

        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            var plan = MethodEmissionPlan.Create(declaration, semanticModel, isKernel: false);
            MethodTranspiler.GenerateCudaSignature(in plan, sink.Add, s_cudaDialect);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cudaDialect, out _);
        }
    }

    private sealed class FailureSink(
        ImmutableArray<Diagnostic>.Builder diagnostics,
        MethodDeclarationSyntax declaration,
        bool isKernel,
        bool isDeviceIteratorCandidate,
        bool isValidDeviceIterator)
    {
        public void Add(TranspileFailure failure)
        {
            if (failure.Descriptor.Id == GSharpDiagnostics.UnsupportedSyntax.Id &&
                failure.MessageArgs is
                    [nameof(SyntaxKind.YieldReturnStatement) or nameof(SyntaxKind.YieldBreakStatement)] &&
                IsYieldAlreadyReportedByBarrierValidator(failure.Location))
            {
                return;
            }

            if (isDeviceIteratorCandidate &&
                (failure.Descriptor.Id == GSharpDiagnostics.KernelReturnMustBeVoidOrBlittable.Id ||
                 failure.Descriptor.Id == GSharpDiagnostics.InstanceDeviceFunctionNotSupported.Id))
            {
                return;
            }

            diagnostics.Add(Diagnostic.Create(failure.Descriptor, failure.Location, failure.MessageArgs));
        }

        private bool IsYieldAlreadyReportedByBarrierValidator(Location location)
        {
            if (isKernel || !isValidDeviceIterator)
            {
                return true;
            }

            if (!location.IsInSource || location.SourceTree != declaration.SyntaxTree)
            {
                return false;
            }

            var yieldStmt = declaration.FindNode(location.SourceSpan)
                .FirstAncestorOrSelf<YieldStatementSyntax>();
            if (yieldStmt is null)
            {
                return false;
            }

            for (var ancestor = yieldStmt.Parent;
                 ancestor != null && ancestor != declaration;
                 ancestor = ancestor.Parent)
            {
                if (ancestor is LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
                {
                    return true;
                }
            }

            return KernelBarrierValidator.TryGetUnflattenableYieldConstruct(yieldStmt, declaration, out _);
        }
    }
}
