namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Warns about a static <c>SharedMemory&lt;T&gt;</c> declaration over the recommended size (GSH0013) and reports a
/// <c>SharedMemory&lt;T&gt;</c> whose count is a negative constant (GSH0117).
/// </summary>
internal sealed class SharedMemorySizeRule :
    ISyntaxValidationRule<LocalDeclarationStatementSyntax>,
    ISyntaxValidationRule<BaseObjectCreationExpressionSyntax>
{
    public static readonly SharedMemorySizeRule Instance = new();

    private const int MaxSharedMemoryWarningBytes = 32768;

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            GSharpDiagnostics.ExcessiveSharedMemoryWarning,
            GSharpDiagnostics.SharedMemorySizeNegative
        );

    private SharedMemorySizeRule()
    {
    }

    // Each declaration is checked on its own, not the kernel's total; a size that is not a constant is dynamic shared memory
    public void Analyze(LocalDeclarationStatementSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node.Declaration.Type) is not INamedTypeSymbol { TypeArguments.Length: 1 } type ||
            !type.IsSharedMemoryType())
        {
            return;
        }

        var elementSize = TypeLayoutCalculator.GetDeviceLayout(type.TypeArguments[0], context.Dialect).Size;
        foreach (var variable in node.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not { } value ||
                !InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(value, out var size) ||
                size == null ||
                context.SemanticModel.GetSafeConstantValue(size) is not { HasValue: true } count)
            {
                continue;
            }

            // long: an int count times the element size can pass int.MaxValue and wrap below the limit
            var bytes = Convert.ToInt64(count.Value ?? 0) * elementSize;
            if (bytes > MaxSharedMemoryWarningBytes)
            {
                context.ReportDiagnostic(GSharpDiagnostics.ExcessiveSharedMemoryWarning, size, bytes,
                    MaxSharedMemoryWarningBytes);
            }
        }
    }

    // C# rejects an array of a negative constant size (CS0248); a shared memory declaration of a negative count has no
    // meaning on any path either, and a constant one is known here. Every creation is checked, not only a local's
    // initializer. A count of 0 declares empty shared memory, and a count known only at run time is checked when the
    // kernel runs
    public void Analyze(BaseObjectCreationExpressionSyntax node, ref ValidationContext context)
    {
        if (context.SemanticModel.GetTypeSymbol(node) is INamedTypeSymbol { TypeArguments.Length: 1 } type &&
            type.IsSharedMemoryType() &&
            DynamicSharedMemoryAnalysis.TryGetCount(
                node,
                context.SemanticModel,
                out var count,
                out var constantCount
            ) &&
            constantCount is int negative && negative < 0)
        {
            context.ReportDiagnostic(GSharpDiagnostics.SharedMemorySizeNegative, count, negative);
        }
    }
}
