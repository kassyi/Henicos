using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Helpers;

/// <summary>
/// Writes the C++ forms of the integer operators that <see cref="IntegerSemantics"/> chose (#722): a constant as its
/// value, a Prelude call, a masked shift count, and the steps and compound assignments that write their target twice.
/// </summary>
internal static class IntegerSemanticsEmitter
{
    /// <summary>
    /// Writes <paramref name="operation"/> (of <paramref name="node"/>) as its value when
    /// <see cref="IntegerSemantics.GetConstant"/> gives one (<c>3 &lt;&lt; 33</c> is <c>6</c>, a negative value
    /// parenthesized), with the type C# gives it where it is used (<see cref="ConstantAtUse"/>); returns whether it did.
    /// </summary>
    public static bool TryAppendConstant(IOperation? operation, ExpressionSyntax node, ref TranspileContext context)
    {
        if (IntegerSemantics.GetConstant(operation) is not var (type, value))
        {
            return false;
        }

        ConstantAtUse.Append(node, type, value, ref context);
        return true;
    }

    /// <summary>
    /// Writes the count of a shift whose mask is <paramref name="mask"/>: a constant in range as written, any other
    /// constant masked (<c>33</c> is <c>1</c>), and anything else as <c>(&lt;count&gt;&amp;31)</c>.
    /// </summary>
    public static void AppendShiftCount(IOperation? count, ExpressionSyntax syntax, int mask, ref TranspileContext context)
    {
        if (count?.ConstantValue is { HasValue: true, Value: int constant })
        {
            if ((constant & mask) == constant)
            {
                context.Visitor.Visit(syntax);
                return;
            }

            context.Emitter.Writer.Append(constant & mask);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            AppendOperand(Unparenthesize(syntax), ref context);
            context.Emitter.Writer.Append($"&{mask}");
        }
    }

    /// <summary>
    /// Writes the increment or decrement <paramref name="operation"/> (<paramref name="node"/>, of
    /// <paramref name="operand"/>), for which <see cref="IntegerSemantics.GetIncrementHelper"/> gave
    /// <paramref name="step"/>: <c>x=__gsharp_add_i32(x,1)</c> for a statement or a prefix step (parenthesized where its
    /// value is used), and <c>__gsharp_sub_i32((x=__gsharp_add_i32(x,1)),1)</c> for a postfix step whose value is used
    /// (the old value, as the step wraps). The target is written twice; the parts of it whose evaluation writes something
    /// are evaluated into temporaries first (<see cref="AppendWithSpilledTarget"/>).
    /// </summary>
    /// <remarks>
    /// [WHY] The target stays an expression: a <c>T&amp;</c> bound to a member of a packed struct
    /// (<c>#pragma pack(1)</c>) is read as aligned (CUDA error 716).
    /// </remarks>
    public static void AppendStep(IIncrementOrDecrementOperation operation, ExpressionSyntax node,
        ExpressionSyntax operand, string step, ref TranspileContext context)
    {
        var form = !IsValueUsed(node, context.Query.SemanticModel)
            ? StepForm.Statement
            : operation.IsPostfix
                ? StepForm.OldValue
                : StepForm.NewValue;
        var undo = IntegerSemantics.GetStepUndoHelper(operation);
        AppendWithSpilledTarget(operand, step, value: null, cast: null, form, undo, ref context);
    }

    /// <summary>
    /// Writes the compound assignment <paramref name="operation"/> (<paramref name="node"/>), for which
    /// <see cref="IntegerSemantics.GetCompoundHelper"/> gave <paramref name="binaryHelper"/>, as
    /// <c>x=(T)__gsharp_add_i32(x,y)</c> (parenthesized where its value is used; the cast narrows to a target narrower
    /// than the operator), with the parts of the target whose evaluation writes something in temporaries (see
    /// <see cref="AppendStep"/>).
    /// </summary>
    public static void AppendCompound(ICompoundAssignmentOperation operation, AssignmentExpressionSyntax node,
        string binaryHelper, ref TranspileContext context)
    {
        var cast = IntegerSemantics.GetCompoundNarrowing(operation);
        var form = IsValueUsed(node, context.Query.SemanticModel) ? StepForm.NewValue : StepForm.Statement;
        AppendWithSpilledTarget(node.Left, binaryHelper, node.Right, cast, form, undo: null, ref context);
    }

    /// <summary>Writes <c>&lt;helper&gt;(&lt;left&gt;,&lt;right&gt;)</c>.</summary>
    public static void AppendCall(string helper, ExpressionSyntax left, ExpressionSyntax right,
        ref TranspileContext context)
    {
        context.Emitter.Writer.Append(helper);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(left));
            context.Emitter.Writer.Append(',');
            context.Visitor.Visit(Unparenthesize(right));
        }
    }

    /// <summary>Writes <c>&lt;helper&gt;(&lt;operand&gt;)</c> (a negation).</summary>
    public static void AppendUnaryCall(string helper, ExpressionSyntax operand, ref TranspileContext context)
    {
        context.Emitter.Writer.Append(helper);
        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(operand));
        }
    }

    /// <summary>
    /// <paramref name="syntax"/> without the parentheses around it: an argument of a call needs none of its own
    /// (<c>(i+1)*2</c> is <c>__gsharp_mul_i32(__gsharp_add_i32(i,1),2)</c>), and <c>ref (i)</c> names <c>i</c>.
    /// </summary>
    internal static ExpressionSyntax Unparenthesize(ExpressionSyntax syntax)
    {
        while (syntax is ParenthesizedExpressionSyntax parenthesized)
        {
            syntax = parenthesized.Expression;
        }

        return syntax;
    }

    // How the value of a step or a compound assignment is used: not at all, the new value, or the old value of a
    // postfix step
    private enum StepForm
    {
        Statement,
        NewValue,
        OldValue
    }

    // <target>=(<cast>)<helper>(<target>,<value or 1>) in the form <form>. When finding the target writes something
    // (data[k++].B += 3), those parts are evaluated into temporaries first, in the C# order, and the target is written
    // with them (C# spec §12.21.4: the location is found once):
    // ([&]() { int __gsharp_seq_0 = <k++>; return data[__gsharp_seq_0].B=__gsharp_add_i32(data[__gsharp_seq_0].B,3); }())
    private static void AppendWithSpilledTarget(ExpressionSyntax target, string helper, ExpressionSyntax? value,
        ITypeSymbol? cast, StepForm form, string? undo, ref TranspileContext context)
    {
        var spills = ImmutableArray.CreateBuilder<(ExpressionSyntax Expression, bool Reference)>();
        CollectSpills(target, context.Query.SemanticModel, spills);
        if (spills.Count == 0)
        {
            AppendStepForm(target, helper, value, cast, form, undo, inSequence: false, ref context);
            return;
        }

        var spilled = context.Emitter.SpilledExpressions;
        try
        {
            using var sequence = EvaluationOrder.BeginSequence(context.Emitter.Writer);
            foreach (var (expression, reference) in spills)
            {
                spilled[expression] = EvaluationOrder.DeclareTemp(ref context, expression, reference);
            }

            sequence.Return();
            AppendStepForm(target, helper, value, cast, form, undo, inSequence: true, ref context);
        }
        finally
        {
            foreach (var (expression, _) in spills)
            {
                spilled.Remove(expression);
            }
        }
    }

    // The parts of a target whose evaluation writes something, in the C# order: an index or a pointer (spilled as its
    // value), or any other part that is not the receiver of an element or member access (spilled as a reference to the
    // location it names, auto&&, as the sequenced compound assignment does)
    private static void CollectSpills(ExpressionSyntax target, SemanticModel model,
        ImmutableArray<(ExpressionSyntax Expression, bool Reference)>.Builder spills)
    {
        switch (target)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                CollectSpills(parenthesized.Expression, model, spills);
                return;
            case ElementAccessExpressionSyntax element:
                CollectSpills(element.Expression, model, spills);
                element.ArgumentList.Arguments
                    .AsStackEnumerable()
                    .Where(model, static (argument, m) => Writes(argument.Expression, m))
                    .Select(static argument => (argument.Expression, false))
                    .AddTo(spills);

                return;
            case MemberAccessExpressionSyntax member:
                CollectSpills(member.Expression, model, spills);
                return;
            case PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PointerIndirectionExpression } indirection:
                if (Writes(indirection.Operand, model))
                {
                    spills.Add((indirection.Operand, false));
                    return;
                }

                CollectSpills(indirection.Operand, model, spills);
                return;
            default:
                if (Writes(target, model))
                {
                    spills.Add((target, true));
                }

                return;
        }
    }

    private static bool Writes(ExpressionSyntax expression, SemanticModel model) =>
        !ExpressionEffects.Of(expression, model).Writes.IsEmpty;

    // The step itself; its value is parenthesized where it is a part of an expression (not the value a sequence returns)
    private static void AppendStepForm(ExpressionSyntax target, string helper, ExpressionSyntax? value,
        ITypeSymbol? cast, StepForm form, string? undo, bool inSequence, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (form == StepForm.Statement || (form == StepForm.NewValue && inSequence))
        {
            AppendRepeatedAssignment(target, helper, value, cast, ref context);
            return;
        }

        if (form == StepForm.NewValue)
        {
            using (writer.Parentheses())
            {
                AppendRepeatedAssignment(target, helper, value, cast, ref context);
            }

            return;
        }

        // The value before the step: the new value stepped back, which wraps back as well
        writer.Append(undo ?? helper);
        using (writer.Parentheses())
        {
            using (writer.Parentheses())
            {
                AppendRepeatedAssignment(target, helper, value, cast, ref context);
            }

            writer.Append(",1");
        }
    }

    // <target>=(<cast>)<helper>(<target>,<value or 1>)
    private static void AppendRepeatedAssignment(ExpressionSyntax target, string helper, ExpressionSyntax? value,
        ITypeSymbol? cast, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        context.Visitor.Visit(target);
        writer.Append('=');
        if (cast != null)
        {
            using (writer.Parentheses())
            {
                context.Emitter.Dialect.AppendType(ref context, cast);
            }
        }

        writer.Append(helper);
        using (writer.Parentheses())
        {
            context.Visitor.Visit(Unparenthesize(target));
            writer.Append(',');
            if (value == null)
            {
                writer.Append('1');
            }
            else
            {
                context.Visitor.Visit(Unparenthesize(value));
            }
        }
    }

    /// <summary>
    /// Writes <paramref name="syntax"/> as the left operand of <c>&amp;</c>, parenthesized when its C++ binds more
    /// loosely (an assignment, <c>|</c>, <c>^</c>, <c>&amp;&amp;</c>, <c>||</c>, <c>??</c>); <c>?:</c> is written in
    /// parentheses of its own (ConditionalExpressionTranspilerRule).
    /// </summary>
    private static void AppendOperand(ExpressionSyntax syntax, ref TranspileContext context)
    {
        var loose = syntax is AssignmentExpressionSyntax or
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.BitwiseOrExpression or (int)SyntaxKind.ExclusiveOrExpression or
                (int)SyntaxKind.LogicalAndExpression or (int)SyntaxKind.LogicalOrExpression or
                (int)SyntaxKind.CoalesceExpression };
        if (!loose)
        {
            context.Visitor.Visit(syntax);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Visitor.Visit(syntax);
        }
    }

    // Whether the value of an increment or assignment is read: not when it is a statement, a part of a for loop or the
    // body of a lambda that returns nothing (the body of Gpu.ForUnroll(0, 4, i => sum += i))
    private static bool IsValueUsed(ExpressionSyntax node, SemanticModel model) => node.Parent switch
    {
        ExpressionStatementSyntax or ForStatementSyntax => false,
        AnonymousFunctionExpressionSyntax lambda =>
            model.GetSymbol(lambda) is not IMethodSymbol { ReturnsVoid: true },
        _ => true
    };
}
