namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>
/// A conditional jump in the linear form: <c>if (&lt;condition&gt;) goto &lt;target&gt;;</c> when
/// <paramref name="jumpIfTrue"/> is <see langword="true"/>, or <c>if (!(&lt;condition&gt;)) goto &lt;target&gt;;</c>
/// when <see langword="false"/>.
/// </summary>
/// <param name="condition">The boolean condition expression.</param>
/// <param name="jumpIfTrue">Whether the jump is taken when the condition holds.</param>
/// <param name="target">The label symbol jumped to.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">Whether the condition is uniform across a warp.</param>
internal sealed class IrConditionalGoto(IrExpression condition, bool jumpIfTrue, IrLabelSymbol target,
    IrSource source, IrUniformity uniformity = IrUniformity.Unclassified) : IrStatement(source)
{
    /// <summary>The boolean condition expression.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>Whether the jump is taken when <see cref="Condition"/> holds.</summary>
    public bool JumpIfTrue { get; } = jumpIfTrue;

    /// <summary>The label symbol jumped to.</summary>
    public IrLabelSymbol Target { get; } = target;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This node with <paramref name="condition"/>; this node itself when it is the same.</summary>
    public IrConditionalGoto Update(IrExpression condition) =>
        condition == Condition
            ? this
            : KeepSpelling(new IrConditionalGoto(condition, JumpIfTrue, Target, Source, Uniformity));

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrConditionalGoto WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity
            ? this
            : KeepSpelling(new IrConditionalGoto(Condition, JumpIfTrue, Target, Source, uniformity));
}
