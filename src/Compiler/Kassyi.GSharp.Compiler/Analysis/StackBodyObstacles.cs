namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The shapes of a method that the CPU stack models cannot write from the body lowered for the stacks
/// (<see cref="Lowering.AstLoweringPipeline.LowerForStack"/>): the native and the managed stack model both keep such a
/// method on its state machine.
/// </summary>
internal static class StackBodyObstacles
{
    /// <summary>
    /// Whether <paramref name="node"/> declares a local with <c>using</c> (<c>using var d = ...;</c>).
    /// </summary>
    /// <remarks>
    /// A walk of a body that classifies its nodes once tests each node with this;
    /// <see cref="DeclaresUsingWithActiveMask"/> walks the body itself.
    /// </remarks>
    public static bool IsUsingDeclaration(SyntaxNode node) =>
        node is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword };

    /// <summary>
    /// Whether <paramref name="method"/> reaches <c>Warp.ActiveMask()</c> and its body declares a local with
    /// <c>using</c>.
    /// </summary>
    /// <remarks>
    /// [WHY] A method that reaches Warp.ActiveMask() gets the reconvergence regions, whose gotos (a continue that leaves
    /// the region of a loop body) go to a label after the statements they skip. A using declaration among those
    /// statements makes such a goto jump past it, which C# forbids (CS8648) and C++ forbids as a jump into the try that
    /// the declaration becomes.
    /// </remarks>
    /// <param name="method">The kernel or device function.</param>
    /// <param name="compilation">The compilation that holds its declaration.</param>
    public static bool DeclaresUsingWithActiveMask(IMethodSymbol method, Compilation compilation)
    {
        if (!SuspensionAnalysis.ReachesActiveMaskOnStack(method, compilation) ||
            !method.TryGetDeclaringSyntax<MethodDeclarationSyntax>(out var declaration) ||
            !compilation.ContainsSyntaxTree(declaration.SyntaxTree))
        {
            return false;
        }

        var semanticModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
        return RemovedCalls.DescendantNodesThatRun(declaration, semanticModel)
            .AsStackEnumerable()
            .Any(IsUsingDeclaration);
    }
}
