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
    public static void AddFailures(MethodDeclarationSyntax declaration, IMethodSymbol method, SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var sink = new FailureSink(diagnostics);

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

    private sealed class FailureSink(ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        public void Add(TranspileFailure failure) =>
            diagnostics.Add(Diagnostic.Create(failure.Descriptor, failure.Location, failure.MessageArgs));
    }
}
