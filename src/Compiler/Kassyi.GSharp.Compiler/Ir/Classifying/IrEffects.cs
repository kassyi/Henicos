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
/// </remarks>
internal static class IrEffects
{
    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects Of(IrExpression expression, Compilation compilation)
    {
        var walker = new IrEffectWalker(compilation);
        walker.AddValue(expression);
        return walker.ToEffects();
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names: the reads of its index and
    /// receiver, without reading the location. With <paramref name="readsValue"/> the location is also read (the target
    /// of a compound assignment).
    /// </summary>
    /// <exception cref="InvalidOperationException">A call in the expression is not classified.</exception>
    public static ExpressionEffects OfLocation(IrExpression expression, Compilation compilation,
        bool readsValue = false)
    {
        var walker = new IrEffectWalker(compilation);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.ReadRoot(root);
        }

        return walker.ToEffects();
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
        return walker.ToEffects();
    }
}
