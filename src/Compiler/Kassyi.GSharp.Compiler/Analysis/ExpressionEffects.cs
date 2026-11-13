namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What evaluating an expression reads (<see cref="Reads"/>) and writes (<see cref="Writes"/>), and whether it may throw
/// (<see cref="MayThrow"/>), as far as the order of the operands of an enclosing expression can tell
/// (<see cref="EvaluationOrder"/>).
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
/// <para>
/// A check that may throw (<see cref="ExpressionTraps"/>: the null check of an array read, a bounds check, an integer
/// division, a checked operation, the value of a <c>Nullable&lt;T&gt;</c>, a call) is ordered only against a write to
/// <see cref="VariableSet.Memory"/> in another operand (<see cref="ThrowInterferesWith"/>), never against a read, which
/// leaves nothing to see after the exception. A write to a local is left out as well: a kernel has no <c>try</c>
/// (GSH0038), so a thread that throws ends there, and its locals with it.
/// </para>
/// </remarks>
internal readonly struct ExpressionEffects(VariableSet reads, VariableSet writes, bool mayThrow = false)
{
    public VariableSet Reads { get; } = reads;

    public VariableSet Writes { get; } = writes;

    /// <summary>
    /// Whether evaluating the expression may raise an exception that C# defines (<see cref="ExpressionTraps"/>), or a
    /// check that G# places there for one (the null check of an array argument of a device function).
    /// </summary>
    public bool MayThrow { get; } = mayThrow;

    /// <summary>
    /// Neither reads, writes nor may throw (a constant, <c>ThreadIndex.X</c>, a lambda): it may stay in place while
    /// other operands move into temporaries.
    /// </summary>
    public bool IsEmpty => Reads.IsEmpty && Writes.IsEmpty && !MayThrow;

    /// <summary>The effects of evaluating <paramref name="expression"/> for its value.</summary>
    public static ExpressionEffects Of(ExpressionSyntax expression, SemanticModel model)
    {
        var walker = new EffectWalker(model);
        walker.AddValue(expression);
        return new ExpressionEffects(walker.Reads, walker.Writes, ExpressionTraps.MayTrap(expression, model));
    }

    /// <summary>
    /// The effects of evaluating <paramref name="expression"/> to the location it names (the target of an assignment, a
    /// <c>ref</c> / <c>out</c> / <c>in</c> argument): the reads of its index and receiver, without reading the location.
    /// With <paramref name="readsValue"/> the location is also read (the target of a compound assignment). An element
    /// of an array is checked as it is named (a <c>ref</c> argument, the target of a compound assignment).
    /// </summary>
    public static ExpressionEffects OfLocation(ExpressionSyntax expression, SemanticModel model, bool readsValue = false)
    {
        var walker = new EffectWalker(model);
        var root = walker.AddLocation(expression);
        if (readsValue)
        {
            walker.AddRead(root);
        }

        return new ExpressionEffects(walker.Reads, walker.Writes, ExpressionTraps.MayTrap(expression, model));
    }

    /// <summary>
    /// The effects of evaluating <paramref name="target"/>, the target of a simple assignment, to its location:
    /// <see cref="OfLocation"/>, except that the null and bounds check of an array element of a primitive or reference
    /// type is not part of it, since C# makes it when it stores the value, after the right operand
    /// (<see cref="ExpressionTraps.MayTrapBeforeStore"/>).
    /// </summary>
    public static ExpressionEffects OfAssignmentTarget(ExpressionSyntax target, SemanticModel model)
    {
        var walker = new EffectWalker(model);
        walker.AddLocation(target);
        return new ExpressionEffects(walker.Reads, walker.Writes, ExpressionTraps.MayTrapBeforeStore(target, model));
    }

    /// <summary>These effects, and a check that may throw when <paramref name="mayThrow"/> is set.</summary>
    public ExpressionEffects WithThrow(bool mayThrow) =>
        mayThrow && !MayThrow ? new ExpressionEffects(Reads, Writes, mayThrow: true) : this;

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

        // A call of user code may throw (ExpressionTraps)
        return new ExpressionEffects(walker.Reads, walker.Writes, mayThrow: true);
    }

    /// <summary>What evaluating this and <paramref name="other"/> reads and writes, and whether one may throw.</summary>
    public ExpressionEffects Union(in ExpressionEffects other) =>
        new(Reads.Union(other.Reads), Writes.Union(other.Writes), MayThrow || other.MayThrow);

    /// <summary>
    /// Whether this operand, evaluated before <paramref name="later"/>, could see or change what it does
    /// (<see cref="DataInterferesWith"/>), or the C# order decides whether a write happens before an exception
    /// (<see cref="ThrowInterferesWith"/>).
    /// </summary>
    public bool InterferesWith(in ExpressionEffects later) => DataInterferesWith(later) || ThrowInterferesWith(later);

    /// <summary>
    /// Whether this operand, evaluated before <paramref name="later"/>, writes what it reads or writes, or reads what it
    /// writes: evaluated in the other order, one of them would see another value.
    /// </summary>
    public bool DataInterferesWith(in ExpressionEffects later) =>
        Writes.Overlaps(later.Reads) || Writes.Overlaps(later.Writes) || Reads.Overlaps(later.Writes);

    /// <summary>
    /// Whether one of the two operands writes <see cref="VariableSet.Memory"/> and the other may throw
    /// (<see cref="MayThrow"/>): evaluated in the other order, the write would be lost before the exception or made
    /// although the exception comes first (#1786 (a throwing check in a later operand runs before an earlier call on
    /// MSVC)). Two reads, a read and a check, or a check and a write to a local never interfere this way.
    /// </summary>
    public bool ThrowInterferesWith(in ExpressionEffects later) =>
        (Writes.Memory && later.MayThrow) || (MayThrow && later.Writes.Memory);
}
