using Kassyi.GSharp.Compiler.Analysis;

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
/// the meaning: a call of a struct member through the implicit <see langword="this"/> writes <see langword="this"/> as
/// an explicit receiver does; a discard is written by no one; the location of <see langword="this"/> and of a field of
/// a struct temporary reads nothing; the member an object initializer assigns belongs to the new value; the receiver of
/// an extension method is an argument like the others (a by-value receiver is not written, and an array receiver
/// makes the call more than scalar math); a struct property used as a receiver calls its getter; a user-defined
/// operator writes what its body writes, and a user-defined conversion of a constant is a call. An opaque node may read
/// and write anything. <c>IrClassificationEquivalenceTests</c> compares both walks over every GPU method of the tests
/// and names each of these differences.
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
            walker.AddRead(root);
        }

        return walker.ToEffects();
    }
}
