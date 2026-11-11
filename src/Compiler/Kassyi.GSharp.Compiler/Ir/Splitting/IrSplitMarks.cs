using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Which nodes of one expression hold a call that splits, found once per statement for <see cref="IrSpillPlanner"/>:
/// anywhere below them, and outside the conditionally evaluated operands of <c>?:</c>, <c>&amp;&amp;</c>,
/// <c>||</c> and <c>??</c>. Also whether the expression holds what the IR cannot plan.
/// </summary>
/// <remarks>
/// A call that C# removes (<see cref="RemovedCalls"/>) is not evaluated, its arguments included, so nothing below it
/// splits.
/// </remarks>
internal sealed class IrSplitMarks
{
    private readonly HashSet<IrNode> _holds = [];
    private readonly HashSet<IrNode> _holdsOutsideConditional = [];
    private bool _opaqueHoldsSplit;
    private bool _operatorMacro;

    private readonly SyntaxTree _tree;

    private IrSplitMarks(SyntaxTree tree) => _tree = tree;

    /// <summary>The tree the expression is written in.</summary>
    public SyntaxTree Tree => _tree;

    /// <summary>Why the expression cannot be planned when it holds a split; <see cref="IrSpillFallback.None"/>
    /// otherwise.</summary>
    public IrSpillFallback Fallback =>
        _opaqueHoldsSplit ? IrSpillFallback.OpaqueHoldsSplit
        : _operatorMacro ? IrSpillFallback.OperatorMacro
        : IrSpillFallback.None;

    /// <summary>Marks the nodes of <paramref name="expression"/>, written in <paramref name="tree"/>.</summary>
    /// <exception cref="InvalidOperationException">A call or opaque node in it is not classified.</exception>
    public static IrSplitMarks Of(IrExpression expression, SyntaxTree tree)
    {
        var marks = new IrSplitMarks(tree);
        marks.Mark(expression);
        return marks;
    }

    /// <summary>Whether a call below <paramref name="node"/>, or the node itself, splits.</summary>
    public bool Holds(IrNode node) => _holds.Contains(node);

    /// <summary>
    /// Whether <paramref name="node"/> holds a split that runs whenever it is evaluated: one that is not inside a
    /// conditionally evaluated operand below it.
    /// </summary>
    public bool HoldsOutsideConditional(IrNode node) => _holdsOutsideConditional.Contains(node);

    // (holds a split, holds one outside a conditionally evaluated operand)
    private (bool Any, bool Outside) Mark(IrNode node)
    {
        var any = false;
        switch (node)
        {
            case IrCall call when RemovedCalls.IsRemoved(call.Method, _tree):
                return (false, false);
            case IrCall call:
                any = call.Classification.Split.IsSplit;
                break;
            case IrOpaqueExpression opaque:
                any = opaque.Split switch
                {
                    IrOpaqueSplit.HoldsSplit => true,
                    IrOpaqueSplit.None => false,
                    _ => throw new InvalidOperationException("The opaque node has not been classified.")
                };
                _opaqueHoldsSplit |= any;
                break;
            case IrBinary { Operator.Method: not null } or IrUnary { Operator.Method: not null }
                or IrCompoundAssignment { Operator.Method: not null } or IrIncrement { OperatorMethod: not null }:
                _operatorMacro = true;
                break;
        }

        var outside = any;
        foreach (var child in IrNodeChildren.Of(node))
        {
            var (childAny, childOutside) = Mark(child);
            any |= childAny;
            outside |= childOutside && !IsConditionalOperand(node, child);
        }

        if (any)
        {
            _holds.Add(node);
        }

        if (outside)
        {
            _holdsOutsideConditional.Add(node);
        }

        return (any, outside);
    }

    /// <summary>
    /// Whether <paramref name="child"/> is an operand of <paramref name="parent"/> that C# evaluates only on one
    /// branch: an arm of <c>?:</c>, the right operand of <c>&amp;&amp;</c>, <c>||</c> or <c>??</c>.
    /// </summary>
    public static bool IsConditionalOperand(IrNode parent, IrNode child) => parent switch
    {
        IrConditional conditional => conditional.WhenTrue == child || conditional.WhenFalse == child,
        IrLogical logical => logical.Right == child,
        IrCoalesce coalesce => coalesce.WhenNull == child,
        _ => false
    };
}
