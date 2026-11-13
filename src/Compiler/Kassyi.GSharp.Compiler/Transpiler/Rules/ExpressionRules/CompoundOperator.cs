using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// The operator of a compound assignment or of an increment or decrement, as the forms that evaluate its operands into
/// temporaries write it (<c>__gsharp_seq_1</c> and <c>__gsharp_seq_2</c> below): <c>(a&lt;op&gt;b)</c>, a function
/// (<c>__gsharp_add_i32(a,b)</c> for int and long arithmetic, #722; <c>fmodf(a,b)</c> for a floating-point remainder,
/// #727), or a shift whose count is masked (<c>(a&gt;&gt;(b&amp;31))</c>). A division whose divisor may be 0 passes the
/// place of its check last (<c>__gsharp_div_i32(a,b,&lt;site&gt;)</c>, #718).
/// </summary>
/// <param name="Operator">The C++ binary operator (<c>+=</c> gives <c>+</c>).</param>
/// <param name="Helper">The function that replaces the operator, or null.</param>
/// <param name="ShiftMask">The mask of the count of a shift that stays an operator, or 0.</param>
/// <param name="ConvertsValue">
/// Whether the value is converted to the type of the target before the operation: <c>fmod</c> of CUDA and of the C++
/// library is a host-only template for operands of two types (<c>fmod(double, int)</c>), so the value temporary is
/// declared with the target type, as C# converts it.
/// </param>
/// <param name="Site">The place of the divisor check of a division helper (<see cref="DivisorCheck"/>), or null.</param>
internal readonly record struct CompoundOperator(string Operator, string? Helper, int ShiftMask,
    bool ConvertsValue = false, NullCheckSite? Site = null)
{
    /// <summary>
    /// The operator of the compound assignment <paramref name="node"/>, bound as <paramref name="operation"/> (null when
    /// it does not bind), or null for an assignment without a binary operator (<c>=</c>, <c>??=</c>).
    /// </summary>
    public static CompoundOperator? ForAssignment(AssignmentExpressionSyntax node,
        ICompoundAssignmentOperation? operation, ref TranspileContext context)
    {
        if (GetBinaryOperator(node) is not { } binaryOperator)
        {
            return null;
        }

        var remainder = operation == null ? null : FloatingPointRemainder.GetCompoundHelper(operation);
        var helper = operation == null ? null : IntegerSemantics.GetCompoundHelper(operation) ?? remainder;
        var shiftMask = operation != null && helper == null ? IntegerSemantics.GetCompoundShiftMask(operation) : 0;
        return new CompoundOperator(binaryOperator, helper, shiftMask, ConvertsValue: remainder != null,
            DivisorCheck.PlanSite(helper, node, ref context));
    }

    /// <summary>
    /// The operator of the increment or decrement <paramref name="operation"/> at <paramref name="node"/>: the Prelude
    /// function of a step that wraps (<see cref="IntegerSemantics.GetIncrementHelper"/>), otherwise <c>+</c> or
    /// <c>-</c>.
    /// </summary>
    public static CompoundOperator ForStep(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        ref TranspileContext context) =>
        new(operation.Kind == OperationKind.Increment ? "+" : "-",
            IntegerSemantics.GetIncrementHelper(operation, node, ref context), 0);

    /// <summary>
    /// Writes the operation on the temporaries <paramref name="left"/> and <paramref name="right"/>, or on the constant
    /// 1 when <paramref name="right"/> is null (a step).
    /// </summary>
    public void AppendOperation(SourceWriter writer, int left, int? right)
    {
        if (Helper != null)
        {
            writer.Append(Helper);
        }

        using (writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(writer, left);
            writer.Append(Helper != null ? "," : Operator);
            if (right is not { } value)
            {
                writer.Append('1');
                return;
            }

            if (ShiftMask == 0)
            {
                EvaluationOrder.AppendTempName(writer, value);
                DivisorCheck.AppendSiteArgument(writer, Site);
                return;
            }

            using (writer.Parentheses())
            {
                EvaluationOrder.AppendTempName(writer, value);
                writer.Append($"&{ShiftMask}");
            }
        }
    }

    // The binary operator of a compound assignment (+= gives +), or null for one without (??= is handled before).
    // >>>= is >> of an unsigned type; of int and long it is a Prelude call
    private static string? GetBinaryOperator(AssignmentExpressionSyntax node) => node.Kind() switch
    {
        SyntaxKind.AddAssignmentExpression => "+",
        SyntaxKind.SubtractAssignmentExpression => "-",
        SyntaxKind.MultiplyAssignmentExpression => "*",
        SyntaxKind.DivideAssignmentExpression => "/",
        SyntaxKind.ModuloAssignmentExpression => "%",
        SyntaxKind.AndAssignmentExpression => "&",
        SyntaxKind.OrAssignmentExpression => "|",
        SyntaxKind.ExclusiveOrAssignmentExpression => "^",
        SyntaxKind.LeftShiftAssignmentExpression => "<<",
        SyntaxKind.RightShiftAssignmentExpression or SyntaxKind.UnsignedRightShiftAssignmentExpression => ">>",
        _ => null
    };
}
