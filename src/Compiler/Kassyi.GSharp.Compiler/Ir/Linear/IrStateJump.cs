namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// A jump to another state in a state machine: <c>goto case &lt;state&gt;;</c> when <paramref name="condition"/> is
/// <see langword="null"/>, or a conditional <c>goto case &lt;state&gt;;</c> otherwise.
/// </summary>
/// <param name="condition">The branch condition, or <see langword="null"/> for an unconditional jump.</param>
/// <param name="jumpIfTrue">Whether the jump is taken when <paramref name="condition"/> holds.</param>
/// <param name="state">The target state number.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">Whether the condition is uniform across a warp.</param>
internal sealed class IrStateJump(IrExpression? condition, bool jumpIfTrue, int state, IrSource source,
    IrUniformity uniformity = IrUniformity.Unclassified) : IrStatement(source)
{
    /// <summary>The branch condition, or <see langword="null"/> for an unconditional jump.</summary>
    public IrExpression? Condition { get; } = condition;

    /// <summary>Whether the jump is taken when <see cref="Condition"/> holds.</summary>
    public bool JumpIfTrue { get; } = jumpIfTrue;

    /// <summary>The target state number.</summary>
    public int State { get; } = state;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This jump with <paramref name="condition"/>; this node itself when it is the same.</summary>
    /// <exception cref="ArgumentException">
    /// Switching between conditional and unconditional jumps is not allowed.
    /// </exception>
    public IrStateJump Update(IrExpression? condition)
    {
        if ((condition is null) != (Condition is null))
        {
            throw new ArgumentException("A state jump cannot switch between conditional and unconditional.",
                nameof(condition));
        }

        return condition == Condition
            ? this
            : KeepSpelling(new IrStateJump(condition, JumpIfTrue, State, Source, Uniformity));
    }

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrStateJump WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity
            ? this
            : KeepSpelling(new IrStateJump(Condition, JumpIfTrue, State, Source, uniformity));
}
