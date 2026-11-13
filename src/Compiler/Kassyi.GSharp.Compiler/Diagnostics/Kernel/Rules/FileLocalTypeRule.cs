namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Warns about a file-local type that a body names when the managed path has to copy that body into a generated file,
/// where the type is not visible (GSH0119, <see cref="FileLocalNames"/>): a body that suspends or holds a construct
/// that only the managed path rewrites, and a kernel or function whose copy needs such a body.
/// </summary>
/// <remarks>
/// A body that is not copied (the managed path calls the user's method) may name a file-local type, and so may every
/// body on the CUDA dialect, the native CPU path and the C# execution, which copy no C#. An assembly on the CUDA dialect
/// that exports its device code still leaves out the state machines and managed versions that cannot be written, and its
/// records say so (<see cref="ManagedStateMachineAvailability"/>).
/// </remarks>
internal sealed class FileLocalTypeRule : ISyntaxValidationRule<IdentifierNameSyntax>,
    ISyntaxValidationRule<GenericNameSyntax>,
    ISyntaxValidationRule<AssignmentExpressionSyntax>,
    ISyntaxValidationRule<MethodDeclarationSyntax>
{
    public static readonly FileLocalTypeRule Instance = new();

    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.FileLocalTypeInManagedCopy);

    private FileLocalTypeRule()
    {
    }

    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context) =>
        CheckName(node, FileLocalNames.Find(node, context.SemanticModel), ref context);

    public void Analyze(GenericNameSyntax node, ref ValidationContext context) =>
        CheckName(node, FileLocalNames.Find(node, context.SemanticModel), ref context);

    // var (a, b) = p; calls an extension Deconstruct that no name of the body binds to
    public void Analyze(AssignmentExpressionSyntax node, ref ValidationContext context) =>
        CheckName(node.Left, FileLocalNames.FindDeconstructor(node, context.SemanticModel), ref context);

    // The copy of a kernel or function needs the copy of a function that names a file-local type: reported at the
    // method, since no name of its own body names it
    public void Analyze(MethodDeclarationSyntax node, ref ValidationContext context)
    {
        if (context.Dialect.IsCuda ||
            FileLocalNames.FindBlock(context.MethodSymbol, context.SemanticModel.Compilation) is not { } block ||
            SymbolEqualityComparer.Default.Equals(block.Body, context.MethodSymbol.OriginalDefinition))
        {
            return;
        }

        Report(node.Identifier.GetLocation(), block.Type, block.Body, block.Reason, ref context);
    }

    private static void CheckName(SyntaxNode node, INamedTypeSymbol? type, ref ValidationContext context)
    {
        if (type is null ||
            context.Dialect.IsCuda ||
            FileLocalNames.GetCopyReason(context.MethodSymbol, context.SemanticModel.Compilation) is not { } reason)
        {
            return;
        }

        Report(node.GetLocation(), type, context.MethodSymbol, reason, ref context);
    }

    private static void Report(Location location, INamedTypeSymbol type, IMethodSymbol body, string reason,
        ref ValidationContext context) =>
        context.ReportDiagnostic(
            GSharpDiagnostics.FileLocalTypeInManagedCopy,
            location,
            TypeLayoutCalculator.FormatTypeName(type),
            FileLocalNames.FormatMethod(body),
            reason);
}
