namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The states of a flattened body that <see cref="StateLabelPlanner.Plan"/> found: the top-level labels that start a
/// state and the state each of them starts. <see cref="ContextHoister"/> and <see cref="StateMachineLowerer"/> take the
/// same plan, so they agree on the labels and on the numbering.
/// </summary>
/// <param name="stateLabels">The keys (<see cref="LoweringIdentity.ForDeclaration"/>) of the labels that start a state.</param>
/// <param name="labelStates">The state that each of those labels starts.</param>
/// <param name="model">The semantic model that keys the labels, or <see langword="null"/> to key them by name.</param>
internal sealed class StatePlan(
    HashSet<DeclarationKey> stateLabels,
    Dictionary<DeclarationKey, int> labelStates,
    SemanticModel? model)
{
    /// <summary>The state that each label that starts a state starts.</summary>
    public IReadOnlyDictionary<DeclarationKey, int> LabelStates => labelStates;

    /// <summary>Whether <paramref name="labeled"/> starts a state.</summary>
    public bool StartsState(LabeledStatementSyntax labeled) =>
        stateLabels.Contains(LoweringIdentity.ForDeclaration(labeled, model));
}
