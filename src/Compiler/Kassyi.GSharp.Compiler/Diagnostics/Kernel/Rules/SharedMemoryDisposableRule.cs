using Kassyi.GSharp.Compiler.Diagnostics.Host;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Reports a <c>SharedMemory&lt;T&gt;</c> whose element type is <see cref="IDisposable"/> (GSH0069).</summary>
internal sealed class SharedMemoryDisposableRule : ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>
{
    public static readonly SharedMemoryDisposableRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.SharedMemoryDisposableNotSupported);

    private SharedMemoryDisposableRule()
    {
    }

    // Shared memory is released with the block, so no thread can run a Dispose() of its elements
    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node) is INamedTypeSymbol { TypeArguments.Length: 1 } type &&
            type.IsSharedMemoryType() &&
            IsDisposableElement(type.TypeArguments[0]))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.SharedMemoryDisposableNotSupported,
                node,
                type.TypeArguments[0].Name);
        }
    }

    // A type parameter is disposable when one of its constraints is
    private static bool IsDisposableElement(ITypeSymbol element) =>
        element is ITypeParameterSymbol typeParameter
            ? typeParameter.ConstraintTypes
                .AsStackEnumerable()
                .Any(IsDisposableElement)
            : DisposableAnalyzerHelper.IsDisposable(element);
}
