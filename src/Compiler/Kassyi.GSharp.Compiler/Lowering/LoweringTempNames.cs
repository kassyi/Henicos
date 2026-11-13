using Kassyi.GSharp.Compiler.Ir.Splitting;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Gives the temporaries that the passes hoisting split points out of expressions declare their names, one instance per
/// method, shared by the passes so that no two temporaries of a method get the same name.
/// </summary>
/// <remarks>
/// A temporary without a symbol is identified by its name alone (<see cref="DeclarationKey"/>): two temporaries of one
/// name would become one frame field and, in C++, one variable declared twice. The temporaries of a conditional
/// expression written as an <see langword="if"/> are numbered by one counter; the operands, warp operations and calls
/// that suspend share the other.
/// </remarks>
public sealed class LoweringTempNames
{
    private int _conditions;
    private int _values;

    /// <summary>The next name of the temporary of a <c>?:</c>, <c>&amp;&amp;</c> or <c>||</c>.</summary>
    public string NextCondition() => $"{GSharpInternalNames.Lowering.ConditionTempPrefix}{_conditions++}";

    /// <summary>The next name of the temporary of a warp operation that splits.</summary>
    public string NextWarp() => $"{GSharpInternalNames.Lowering.WarpTempPrefix}{_values++}";

    /// <summary>The next name of the temporary of a call that suspends.</summary>
    public string NextCall() => $"{GSharpInternalNames.Lowering.CallTempPrefix}{_values++}";

    /// <summary>The next name of the temporary of an operand evaluated before a split.</summary>
    public string NextArgument() => $"{GSharpInternalNames.Lowering.ArgumentTempPrefix}{_values++}";

    /// <summary>The next name of a temporary that holds <paramref name="kind"/>.</summary>
    internal string Next(IrTemporaryKind kind) => kind switch
    {
        IrTemporaryKind.Argument => NextArgument(),
        IrTemporaryKind.Warp => NextWarp(),
        IrTemporaryKind.Call => NextCall(),
        IrTemporaryKind.Condition => NextCondition(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown temporary kind.")
    };

    /// <summary>
    /// A copy that continues from the same numbers, for names that are given only if a whole statement can be written
    /// (<see cref="Adopt"/>).
    /// </summary>
    internal LoweringTempNames Fork() => new() { _conditions = _conditions, _values = _values };

    /// <summary>Continues from the numbers of <paramref name="fork"/>, whose names were used.</summary>
    internal void Adopt(LoweringTempNames fork)
    {
        _conditions = fork._conditions;
        _values = fork._values;
    }
}
