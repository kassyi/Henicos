namespace Kassyi.GSharp.Compiler.Instantiation;

/// <summary>
/// What the closed copy of a generic declaration changes, decided from the semantic model of the declaration before
/// anything is written (<see cref="GenericCopyPlanner"/>); <see cref="GenericCopyEmitter"/> writes the copy from it.
/// </summary>
/// <param name="Instance">The instance the copy is for.</param>
/// <param name="Rewrites">The nodes written differently, by node index.</param>
/// <param name="Problems">The nodes the planner already knows have no faithful copy.</param>
internal sealed record GenericCopyPlan(
    GenericInstance Instance,
    ImmutableDictionary<int, CopyRewrite> Rewrites,
    ImmutableArray<CopyProblem> Problems);
