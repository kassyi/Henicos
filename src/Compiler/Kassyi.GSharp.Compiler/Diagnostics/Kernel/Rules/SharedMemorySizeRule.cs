namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>Warns about a static <c>SharedMemory&lt;T&gt;</c> declaration over the recommended size (GSH0013).</summary>
internal sealed class SharedMemorySizeRule : ISyntaxValidationRule<LocalDeclarationStatementSyntax>
{
    public static readonly SharedMemorySizeRule Instance = new();

    private const int MaxSharedMemoryWarningBytes = 32768;

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.ExcessiveSharedMemoryWarning);

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

        var elementSize = CudaLayoutRules.GetCudaLayout(type.TypeArguments[0]).Size;
        foreach (var variable in node.Declaration.Variables)
        {
            if (variable.Initializer?.Value is not { } value ||
                !InitializationSyntaxExtractor.TryGetObjectCreationFirstArgument(value, out var size) ||
                size == null ||
                context.SemanticModel.GetSafeConstantValue(size) is not { HasValue: true } count)
            {
                continue;
            }

            var bytes = Convert.ToInt32(count.Value ?? 0) * elementSize;
            if (bytes > MaxSharedMemoryWarningBytes)
            {
                context.ReportDiagnostic(GSharpDiagnostics.ExcessiveSharedMemoryWarning, size, bytes,
                    MaxSharedMemoryWarningBytes);
            }
        }
    }
}
