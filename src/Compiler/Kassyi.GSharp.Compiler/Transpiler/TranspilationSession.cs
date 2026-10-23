namespace Kassyi.GSharp.Compiler.Transpiler;

internal readonly ref struct TranspilationSession
{
    private readonly RoslynToCudaVisitor? _visitor;
    public readonly IMethodSymbol? MethodSymbol;
    public readonly string? ErrorMessage;

    public ref TranspileContext Context
    {
        get
        {
            if (_visitor == null)
            {
                throw new InvalidOperationException(ErrorMessage ?? "Transpilation session failed.");
            }
            return ref _visitor.Context;
        }
    }

    public bool HasError => ErrorMessage != null;

    private TranspilationSession(RoslynToCudaVisitor? visitor, IMethodSymbol? methodSymbol, string? errorMessage)
    {
        _visitor = visitor;
        MethodSymbol = methodSymbol;
        ErrorMessage = errorMessage;
    }

    public static TranspilationSession Begin(
        in MethodEmissionPlan plan,
        Action<Diagnostic>? reportDiagnostic,
        ITranspilerDialect dialect)
    {
        var semanticModel = plan.SemanticModel;
        var methodSymbol = plan.Symbol;
        if (methodSymbol != null && !GSharpDiagnostics.ValidateKernelReturnType(methodSymbol, reportDiagnostic))
        {
#pragma warning disable RS0030 // Allowed: Used for error message generation only (not a hot path).
            var error =
                $"// GSharp Transpilation failed for {methodSymbol.Name}. Return type {methodSymbol.ReturnType.ToDisplayString()} is not supported.";
#pragma warning restore RS0030
            return new TranspilationSession(null, methodSymbol, error);
        }

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        var visitor = RoslynToCudaVisitor.Rent(dialect, semanticModel, reportDiagnostic, bclIntrinsicMap);
        ref var context = ref visitor.Context;

        if (methodSymbol != null)
        {
            if (methodSymbol.HasAttribute(KnownAttributes.SkipLocalsInit) ||
                methodSymbol.ContainingType?.HasAttribute(KnownAttributes.SkipLocalsInit) == true ||
                methodSymbol.ContainingModule?.HasAttribute(KnownAttributes.SkipLocalsInit) == true)
            {
                context.Emitter.SkipLocalsInit = true;
            }

            context.Emitter.IsGpuKernelEntry = plan.IsGpuKernelEntry;
            context.Emitter.IsVoidReturn = plan.ReturnsVoid;
        }

        return new TranspilationSession(visitor, methodSymbol, null);
    }

    public void Dispose() => _visitor?.Dispose();
}
