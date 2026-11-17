using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Ir.Statements;

/// <summary>A <see langword="while"/> loop: the condition is evaluated before every iteration.</summary>
/// <param name="condition">The condition, a <see cref="bool"/> value.</param>
/// <param name="body">The statement repeated while the condition holds.</param>
/// <param name="locals">The locals declared in the condition (<c>out var x</c>), whose scope is one iteration.</param>
/// <param name="source">Where the node comes from.</param>
/// <param name="uniformity">
/// Whether the condition is the same in every thread of a warp; unclassified when built.
/// </param>
internal sealed class IrWhile(IrExpression condition, IrStatement body, ImmutableArray<IrVariable> locals,
    IrSource source, IrUniformity uniformity = IrUniformity.Unclassified) : IrStatement(source)
{
    /// <summary>The condition evaluated before each iteration.</summary>
    public IrExpression Condition { get; } = condition;

    /// <summary>The statement repeated while the condition holds.</summary>
    public IrStatement Body { get; } = body;

    /// <summary>The locals declared in the condition (<c>out var x</c>), whose scope is one iteration.</summary>
    public ImmutableArray<IrVariable> Locals { get; } = locals;

    /// <summary>Whether the condition is the same in every thread of a warp.</summary>
    public IrUniformity Uniformity { get; } = uniformity;

    /// <summary>This node with the given children; this node itself when they are the same.</summary>
    public IrWhile Update(IrExpression condition, IrStatement body) =>
        condition == Condition && body == Body
            ? this
            : KeepSpelling(new IrWhile(condition, body, Locals, Source, Uniformity));

    /// <summary>This node with <paramref name="uniformity"/>; this node itself when it is the same.</summary>
    public IrWhile WithUniformity(IrUniformity uniformity) =>
        uniformity == Uniformity ? this : KeepSpelling(new IrWhile(Condition, Body, Locals, Source, uniformity));
}
