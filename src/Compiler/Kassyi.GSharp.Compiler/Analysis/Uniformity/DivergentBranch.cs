namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>A thread-variant branch of a body (<see cref="UniformityAnalysis.GetDivergentBranches"/>).</summary>
/// <param name="Condition">The syntax of its condition.</param>
/// <param name="HoldsSplitPoint">Whether its influence region holds a call that splits the state machine.</param>
/// <param name="JoinCalls">
/// The calls and object creations evaluated at its immediate post-dominator, where the threads that took either way meet
/// again.
/// </param>
internal sealed record DivergentBranch(SyntaxNode Condition, bool HoldsSplitPoint, ImmutableArray<SyntaxNode> JoinCalls);
