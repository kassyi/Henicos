using Kassyi.GSharp.Compiler.Analysis;
using Kassyi.GSharp.Compiler.Ir.Expressions;

namespace Kassyi.GSharp.Compiler.Ir.Classifying;

/// <summary>
/// What evaluating an IR expression reads and writes (<see cref="ExpressionEffects"/>), from the nodes and the
/// classification of their calls: the facts the evaluation order is decided from.
/// </summary>
/// <remarks>
/// <para>
/// The rules are those of <see cref="ExpressionEffects"/>: a local or by-value parameter is one variable unless it has
/// another name (<see cref="AliasedVariables"/>); every other location is <see cref="VariableSet.Memory"/>, a field of
/// a struct variable is the variable, a call reads memory unless it is scalar math and writes what its classification
/// says. A temporary of a pass is a variable of its own.
/// </para>
/// <para>
/// Where the syntax walk of <see cref="ExpressionEffects"/> reads a shape and the IR knows the meaning, the IR follows
/// the meaning: a call of a struct member, and a computed property read, through the implicit <see langword="this"/>
/// treat <see langword="this"/> as an explicit receiver does, and a struct property used as a receiver calls its
/// getter, as in the syntax walk; a discard is written by no one; the location of <see langword="this"/> and
/// of a field of a struct temporary reads nothing; the member an object initializer assigns belongs to the new value;
/// the receiver of an extension method is an argument like the others, so a by-value receiver is not written.
/// </para>
/// <para>
/// Two rules state what the IR does not know yet. An operator macro (the only user-defined operator G# accepts) is
/// taken as writing memory: what runs is its target, which the node does not name, so a statement that holds one stays
/// on the syntax-based path for its split. Only calls carry a decision (<see cref="IrCall.Classification"/>); for
/// properties, operators, conversions, creations and aliased locals the walk asks the symbol each time, and whether a
/// creation is a <c>SharedMemory&lt;T&gt;</c> is a type check each time. An opaque node may read and write anything.
/// <c>IrClassificationEquivalenceTests</c> compares both walks over every GPU method and struct member of the tests and
/// names each difference.
/// </para>
/// <para>
/// <see cref="ExpressionEffects.MayThrow"/> follows from <see cref="IrTraps"/> (the same cases as
/// <see cref="ExpressionTraps"/>). The length of an array is counted as throwing unless the overloads that take
/// <see cref="IrTrapReading.Decided"/> read the non-null proofs of the decided tree (the sequence pass); the split
/// plans run before the decisions and keep the conservative rule.
/// </para>
/// </remarks>
internal static class IrEffects
{
    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects Of(IrExpression expression, Compilation compilation) =>
        Of(expression, compilation, IrTrapReading.Undecided);

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> for its value, with the trap rules of
    /// <paramref name="reading"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects Of(IrExpression expression, Compilation compilation, IrTrapReading reading)
    {
        var walker = new IrEffectWalker(compilation);
        walker.AddValue(expression);
        return walker.ToEffects().WithThrow(IrTraps.MayTrap(expression, reading));
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names: the reads of its index and
    /// receiver, without reading the location. With <paramref name="readsValue"/> the location is also read (the target
    /// of a compound assignment).
    /// </summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects OfLocation(IrExpression expression, Compilation compilation,
        bool readsValue = false) =>
        OfLocation(expression, compilation, readsValue, IrTrapReading.Undecided);

    /// <summary>
    /// <see cref="OfLocation(IrExpression, Compilation, bool)"/> with the trap rules of <paramref name="reading"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects OfLocation(IrExpression expression, Compilation compilation, bool readsValue,
        IrTrapReading reading)
    {
        var walker = new IrEffectWalker(compilation);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.ReadRoot(root);
        }

        return walker.ToEffects().WithThrow(IrTraps.MayTrap(expression, reading));
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names and of writing that location:
    /// a <see langword="ref"/> or <see langword="out"/> argument, an assignment target.
    /// </summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects OfWrittenLocation(IrExpression expression, Compilation compilation)
    {
        var walker = new IrEffectWalker(compilation);
        walker.WriteRoot(walker.AddLocation(expression));
        return walker.ToEffects().WithThrow(IrTraps.MayTrap(expression));
    }

    /// <summary>
    /// The effects of evaluating <paramref name="target"/> to the location an assignment writes: the reads of its index
    /// and receiver, and whether evaluating the target may trap before the value is stored.
    /// </summary>
    public static ExpressionEffects OfAssignmentTarget(IrExpression target, Compilation compilation) =>
        OfAssignmentTarget(target, compilation, IrTrapReading.Undecided);

    /// <summary>
    /// <see cref="OfAssignmentTarget(IrExpression, Compilation)"/> with the trap rules of <paramref name="reading"/>.
    /// </summary>
    public static ExpressionEffects OfAssignmentTarget(IrExpression target, Compilation compilation,
        IrTrapReading reading)
    {
        var walker = new IrEffectWalker(compilation);
        walker.AddLocation(target);
        return walker.ToEffects().WithThrow(IrTraps.MayTrapBeforeStore(target, reading));
    }

    /// <summary>
    /// The variable that the location <paramref name="location"/> belongs to, as a read of it alone (without the reads
    /// of its indices and receivers): what reading the target of a compound assignment reads
    /// (<see cref="ExpressionEffects.RootOf"/> over the IR).
    /// </summary>
    public static VariableSet RootOf(IrExpression location, Compilation compilation)
    {
        var walker = new IrEffectWalker(compilation);
        var root = default(VariableSet);
        if (walker.AddLocation(location) is { } effectRoot)
        {
            root.AddRoot(effectRoot);
        }

        return root;
    }
}
