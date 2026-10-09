
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
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic = null,
        ITranspilerDialect? dialect = null)
    {
        if (methodDecl is null)
        {
            throw new ArgumentNullException(nameof(methodDecl));
        }

        if (semanticModel is null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        if (methodSymbol != null && !GSharpDiagnostics.ValidateKernelReturnType(methodSymbol, reportDiagnostic))
        {
#pragma warning disable RS0030 // Allowed: Used for error message generation only (not a hot path).
            var error =
                $"// GSharp Transpilation failed for {methodSymbol.Name}. Return type {methodSymbol.ReturnType.ToDisplayString()} is not supported.";
#pragma warning restore RS0030
            return new TranspilationSession(null, methodSymbol, error);
        }

        var bclIntrinsicMap = BclIntrinsicMapBuilder.GetOrBuildMap(semanticModel.Compilation);
        dialect ??= new GSharpCudaDialect();

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

            context.Emitter.IsGpuKernelEntry = methodSymbol.HasAttribute(KnownAttributes.GpuKernel);
            context.Emitter.IsVoidReturn = methodSymbol.ReturnType.SpecialType == SpecialType.System_Void;
        }

        return new TranspilationSession(visitor, methodSymbol, null);
    }

    public void Dispose() => _visitor?.Dispose();
}
