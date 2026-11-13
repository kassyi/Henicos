namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports a file-local type named in a body that the managed path copies into a generated file, where the type is not
/// visible (GSH0119, <see cref="FileLocalNames"/>): the body of a kernel, of a suspending device function, or of a device
/// function with a managed version, on the dialect that emits managed bodies.
/// </summary>
/// <remarks>
/// The CUDA dialect transpiles the bodies to C++ and needs no copy. An assembly on it that exports its device code still
/// emits the state machines and managed versions for its consumers; one that cannot be written is left out and its
/// record says so, as for the other reasons a copy cannot compile (<see cref="ManagedStateMachineAvailability"/>).
/// </remarks>
internal sealed class FileLocalTypeRule : ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<GenericNameSyntax>
{
    public static readonly FileLocalTypeRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.FileLocalTypeInManagedCopy);

    private FileLocalTypeRule()
    {
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context) => Check(node, ref context);

    public void Analyze(GenericNameSyntax node, ref ValidationContext context) => Check(node, ref context);

    private static void Check(SimpleNameSyntax node, ref ValidationContext context)
    {
        if (context.Dialect.IsCuda ||
            !IsCopied(context.MethodSymbol, context.SemanticModel.Compilation) ||
            FileLocalNames.Find(node, context.SemanticModel) is not { } type)
        {
            return;
        }

        context.ReportDiagnostic(
            GSharpDiagnostics.FileLocalTypeInManagedCopy,
            node.GetLocation(),
            TypeLayoutCalculator.FormatTypeName(type),
            FileLocalNames.FormatMethod(context.MethodSymbol));
    }

    // A kernel has a managed fallback; a device function is copied when it suspends or needs a managed version
    private static bool IsCopied(IMethodSymbol method, Compilation compilation) =>
        method.HasAttribute(KnownAttributes.GpuKernel) ||
        SuspensionAnalysis.RequiresStateMachine(method, compilation) ||
        ManagedVersionAnalysis.Needs(method, compilation);
}
