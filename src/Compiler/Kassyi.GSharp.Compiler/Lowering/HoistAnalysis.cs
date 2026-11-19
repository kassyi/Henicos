namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>The result of <see cref="HoistedVariableAnalysis.Analyze"/> for one body.</summary>
/// <param name="HasSplitPoint">Whether the body has a split point; without one nothing is hoisted or lifted.</param>
/// <param name="Hoisted">The declarators that move into the context, in the order written.</param>
/// <param name="HoistedKeys">The keys of <paramref name="Hoisted"/>.</param>
/// <param name="LiftedConstants">
/// The keys of the const declarators of the body (not of its local functions and lambdas), which are never hoisted and
/// go in front of the state switch instead (<see cref="StateMachineLowerer.LiftedConstants"/>).
/// </param>
internal readonly record struct HoistAnalysis(
    bool HasSplitPoint,
    ImmutableArray<VariableDeclaratorSyntax> Hoisted,
    HashSet<DeclarationKey> HoistedKeys,
    HashSet<DeclarationKey> LiftedConstants)
{
    /// <summary>The result for a body without a split point.</summary>
    public static HoistAnalysis WithoutSplitPoint => new(false, [], [], []);
}
