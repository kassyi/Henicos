namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Runs the transpiler on a kernel or device function as <see cref="GpuKernelGenerator"/> does, for both dialects, and
/// turns what it could not emit (<see cref="TranspileFailure"/>) into diagnostics.
/// </summary>
/// <remarks>
/// [WHY] The same approach as the LibraryImport analyzer of dotnet/runtime, which runs the stub generator and keeps only its
/// diagnostics: whether a construct can be emitted is decided once, by the code that emits it. A syntax kind that no rule
/// handles, including one that a newer C# adds, reaches <see cref="RoslynToCudaVisitor.DefaultVisit"/> and is reported as
/// GSH0003 without a list of rejected kinds; a check that depends on the context or the type is decided as the transpiler
/// decides it. The output is dropped.
/// </remarks>
internal static class TranspilerFailureCheck
{
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();

    private static readonly ITranspilerDialect s_cppDialect = new StandardCppDialect();

    /// <summary>Adds a diagnostic for each failure of the transpilations that the generator runs for <paramref name="method"/>.</summary>
    public static void AddFailures(MethodDeclarationSyntax declaration, IMethodSymbol method, SemanticModel semanticModel,
        ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var sink = new FailureSink(diagnostics);

        if (method.HasAttribute(KnownAttributes.GpuKernel))
        {
            var plan = MethodEmissionPlan.Create(declaration, semanticModel, isKernel: true);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cudaDialect, out _);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cppDialect, out _);
        }

        if (method.HasAttribute(KnownAttributes.GpuDeviceFunction))
        {
            var plan = MethodEmissionPlan.Create(declaration, semanticModel, isKernel: false);
            MethodTranspiler.GenerateCudaSignature(in plan, sink.Add, s_cudaDialect);
            MethodTranspiler.GenerateCudaSignature(in plan, sink.Add, s_cppDialect);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cudaDialect, out _);
            MethodTranspiler.GenerateCudaSource(in plan, sink.Add, s_cppDialect, out _);
        }
    }

    private sealed class FailureSink(ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        public void Add(TranspileFailure failure) =>
            diagnostics.Add(Diagnostic.Create(failure.Descriptor, failure.Location, failure.MessageArgs));
    }
}
