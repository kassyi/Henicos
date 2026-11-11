namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What evaluating an expression reads (<see cref="Reads"/>) and writes (<see cref="Writes"/>), as far as the order of
/// the operands of an enclosing expression can tell (<see cref="EvaluationOrder"/>).
/// </summary>
/// <remarks>
/// <para>
/// A local or by-value parameter is one variable; every other location (array and span elements, pointers, shared
/// memory, <c>ref</c> / <c>out</c> / <c>in</c> parameters, ref locals, static fields, fields of <c>this</c>) is
/// <see cref="VariableSet.Memory"/>. A field of a struct local is the local. A local or parameter that has another name
/// (its address is taken, a <c>ref</c> expression names it, or a lambda or local function captures it) is
/// <see cref="VariableSet.Memory"/> too, since it can be read and written without its name. A call reads <see cref="VariableSet.Memory"/>
/// (its callee may read anything) unless it is pure math over scalars, and writes what
/// <see cref="DeviceFunctionEffects"/> says: nothing, the variables of its <c>ref</c> / <c>out</c> arguments, or
/// <see cref="VariableSet.Memory"/>. Evaluating a <c>ref</c> / <c>out</c> / <c>in</c> argument only names its location
/// (<see cref="OfLocation"/>); a nested call reads the variable of its <c>ref</c> / <c>in</c> arguments and writes the
/// variable of its <c>ref</c> / <c>out</c> arguments as part of its own evaluation. A lambda is not evaluated.
/// </para>
/// <para>
/// [WHY] The same rule as Roslyn's <c>LocalRewriter.IsSafeForReordering</c> and <c>CanChangeValueBetweenReads</c>: a
/// read of a local is safe to reorder unless another operand writes that local. Unlike Roslyn, a call is classified by
/// its callee, since C++ does not keep the order even of positional arguments and most calls would otherwise need
/// temporaries.
/// </para>
/// </remarks>
internal readonly struct ExpressionEffects(VariableSet reads, VariableSet writes)
{
    public VariableSet Reads { get; } = reads;

    public VariableSet Writes { get; } = writes;

    /// <summary>Neither reads nor writes anything (a constant, <c>ThreadIndex.X</c>, a lambda).</summary>
    public bool IsEmpty => Reads.IsEmpty && Writes.IsEmpty;

    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    public static ExpressionEffects Of(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new EffectWalker(model);
        walker.AddValue(expression);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names (the target of an assignment, a
    /// <c>ref</c> / <c>out</c> / <c>in</c> argument): the reads of its index and receiver, without reading the location.
    /// With <paramref name="readsValue"/> the location is also read (the target of a compound assignment).
    /// </summary>
    public static ExpressionEffects OfLocation(ExpressionSyntax expression, SemanticModel model, bool readsValue = false)
    {
        var walker = new EffectWalker(model);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.AddRead(root);
        }

        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>
    /// The variable that the location <paramref name="expression"/> belongs to, as a read of it alone (without the reads
    /// of its indices and receivers): what reading the target of a compound assignment reads.
    /// </summary>
    public static VariableSet RootOf(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new EffectWalker(model);
        var reads = default(VariableSet);
        reads.AddRoot(walker.AddLocation(expression));
        return reads;
    }

    /// <summary>
    /// The effects of a call of <paramref name="target"/> whose arguments are already evaluated, with
    /// <paramref name="referenceArgument"/> (a local or parameter, or <see langword="null"/>) passed by <c>ref</c>: what
    /// evaluating an invocation adds for the call itself. An operator macro expansion uses it for the call of an inner
    /// operator's target with its context.
    /// </summary>
    public static ExpressionEffects OfCall(IMethodSymbol target, ISymbol? referenceArgument, SemanticModel model)
    {
        var walker = new EffectWalker(model);
        CallEffects.AddCallOf(ref walker, target, referenceArgument);
        return new ExpressionEffects(walker.Reads, walker.Writes);
    }

    /// <summary>What evaluating this and <paramref name="other"/> reads and writes.</summary>
    public ExpressionEffects Union(in ExpressionEffects other) =>
        new(Reads.Union(other.Reads), Writes.Union(other.Writes));

    /// <summary>Whether this operand, evaluated before <paramref name="later"/>, could see or change what it does.</summary>
    public bool InterferesWith(in ExpressionEffects later) =>
        Writes.Overlaps(later.Reads) || Writes.Overlaps(later.Writes) || Reads.Overlaps(later.Writes);
}
