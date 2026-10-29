using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles C# assignment expressions into target dialect statements,
/// supporting standard assignments, compound assignments, and deconstructing tuple assignments via immediately-invoked lambdas.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class AssignmentExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<AssignmentExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="AssignmentExpressionTranspilerRule"/>.
    /// </summary>
    public AssignmentExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (node is { Left: TupleExpressionSyntax leftTuple, Right: TupleExpressionSyntax rightTuple })
        {
            // [WHY] Emits an immediately invoked C++ lambda to evaluate RHS tuple elements before assigning to LHS, preventing variable swap clobbering.
            using (context.Emitter.Writer.ImmediatelyInvokedLambda())
            {
                for (var i = 0; i < rightTuple.Arguments.Count; i++)
                {
                    context.Emitter.Writer.Append($"auto {GSharpInternalNames.Lowering.TupleTempPrefix}{i} = ");
                    context.Visitor.Visit(rightTuple.Arguments[i].Expression);
                    context.Emitter.Writer.AppendLine(";");
                }

                for (var i = 0; i < leftTuple.Arguments.Count; i++)
                {
                    context.Visitor.Visit(leftTuple.Arguments[i].Expression);
                    context.Emitter.Writer
                        .Append($" = {GSharpInternalNames.Lowering.TupleTempPrefix}{i}")
                        .AppendLine(";");
                }
            }

            return;
        }

        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            NullRepresentation.AppendCoalesceAssignment(node.Left, node.Right, ref context);
            return;
        }

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (!ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Left, ref context) &&
                node.Right is not RefExpressionSyntax &&
                OperandsInterfere(node, readsTarget: false, ref context, out _))
            {
                TranspileSequencedAssignment(node, ref context);
                return;
            }

            ComputedPropertyGetterCall.TranspileAssignment(node, node.OperatorToken.Text, ref context);
            return;
        }

        if (ComputedPropertyGetterCall.IsComputedPropertyTarget(node.Left, ref context))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node,
                ComputedPropertyGetterCall.CompoundAssignmentToComputedProperty);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // int and long arithmetic that C++ leaves undefined goes through the Prelude, and a shift that stays a C++
        // operator masks its count (#722)
        var compound = context.Query.SemanticModel.GetSafeOperation(node) is ICompoundAssignmentOperation operation &&
                       operation.Syntax.RawKind == node.RawKind
            ? operation
            : null;
        var helper = compound != null ? IntegerSemantics.GetCompoundHelper(compound) : null;
        var shiftMask = compound != null && helper == null ? IntegerSemantics.GetCompoundShiftMask(compound) : 0;

        // C++ has no >>>=; the integer types are handled here (nint and nuint are not)
        if (node.IsKind(SyntaxKind.UnsignedRightShiftAssignmentExpression) && helper == null && shiftMask == 0)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, node.Kind().ToString());
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        if (GetCompoundOperator(node) is { } binaryOperator &&
            OperandsInterfere(node, readsTarget: true, ref context, out var rightEffects))
        {
            TranspileSequencedCompoundAssignment(node, new CompoundOperator(binaryOperator, helper, shiftMask),
                rightEffects, ref context);
            return;
        }

        if (compound != null && helper != null)
        {
            IntegerSemantics.AppendCompound(compound, node, helper, ref context);
            return;
        }

        context.Visitor.Visit(node.Left);
        if (shiftMask != 0)
        {
            // x >>>= n of an unsigned type is x >>= n
            context.Emitter.Writer.Append(node.IsKind(SyntaxKind.LeftShiftAssignmentExpression) ? "<<=" : ">>=");
            IntegerSemantics.AppendShiftCount(compound?.Value, node.Right, shiftMask, ref context);
            return;
        }

        context.Emitter.Writer.Append(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }

    // The operator of a compound assignment as the sequenced form writes it: <op>, a Prelude call (__gsharp_add_i32),
    // or a shift whose count is masked
    private readonly record struct CompoundOperator(string Operator, string? Helper, int ShiftMask);

    // [WHY] C# finds the location of the target first, then (for a compound assignment) reads it, then evaluates the
    // right side, and stores last (C# spec §12.21.2, §12.21.4). C++17 evaluates the right side of an assignment first
    // ([expr.ass]), so the order differs whenever the right side changes what the target reads or the other way round
    // (#608): then the target is bound to a reference and the operands are evaluated in the C# order (EvaluationOrder).
    private static bool OperandsInterfere(AssignmentExpressionSyntax node, bool readsTarget, ref TranspileContext context,
        out ExpressionEffects value)
    {
        var model = context.Query.SemanticModel;
        var target = ExpressionEffects.OfLocation(node.Left, model, readsTarget);
        value = ExpressionEffects.Of(node.Right, model);
        return EvaluationOrder.LastSpilledOperand([target, value]) >= 0;
    }

    // ([&]() { auto&& __gsharp_seq_0 = <left>; <T> __gsharp_seq_1 = <right>; return __gsharp_seq_0 = __gsharp_seq_1; }())
    // The value is converted to the type of the target when it is evaluated (C# spec §12.21.2)
    private static void TranspileSequencedAssignment(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        using var sequence = EvaluationOrder.BeginSequence(context.Emitter.Writer);
        var target = EvaluationOrder.DeclareTemp(ref context, node.Left, reference: true);
        var value = EvaluationOrder.DeclareTemp(ref context, node.Right, reference: false,
            context.Query.SemanticModel.GetTypeSymbol(node.Left));
        sequence.Return();
        EvaluationOrder.AppendTempName(context.Emitter.Writer, target);
        context.Emitter.Writer.Append(" = ");
        EvaluationOrder.AppendTempName(context.Emitter.Writer, value);
    }

    // ([&]() { auto&& __gsharp_seq_0 = <left>; auto __gsharp_seq_1 = __gsharp_seq_0; auto __gsharp_seq_2 = <right>;
    //   return __gsharp_seq_0 = (<T>)(__gsharp_seq_1 <op> __gsharp_seq_2); }())
    // The cast is C#'s x = (T)(x op y), which narrows the promoted result back to the type of the target. The copy of
    // the target is left out (its temporary read in place) when the right side cannot write the variable it belongs to.
    // The operation is <helper>(__gsharp_seq_1,__gsharp_seq_2) for int and long arithmetic, and a shift masks its count:
    // __gsharp_seq_1>>(__gsharp_seq_2&31) (#722)
    private static void TranspileSequencedCompoundAssignment(AssignmentExpressionSyntax node,
        CompoundOperator binaryOperator, in ExpressionEffects rightEffects, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var model = context.Query.SemanticModel;
        var readsBeforeRight = rightEffects.Writes
                                   .Overlaps(ExpressionEffects.RootOf(node.Left, model)) ||
                               NeedsDecltype(model.GetTypeSymbol(node.Left));
        using var sequence = EvaluationOrder.BeginSequence(writer);
        var target = EvaluationOrder.DeclareTemp(ref context, node.Left, reference: true);
        var current = readsBeforeRight ? EvaluationOrder.DeclareCopy(ref context, target) : target;
        var value = EvaluationOrder.DeclareTemp(ref context, node.Right, reference: false);
        sequence.Return();
        EvaluationOrder.AppendTempName(writer, target);
        writer.Append(" = ");
        using (writer.Parentheses())
        {
            AppendTargetType(node.Left, current, ref context);
        }

        if (binaryOperator.Helper != null)
        {
            writer.Append(binaryOperator.Helper);
        }

        using (writer.Parentheses())
        {
            EvaluationOrder.AppendTempName(writer, current);
            writer.Append(binaryOperator.Helper != null ? "," : binaryOperator.Operator);
            if (binaryOperator.ShiftMask == 0)
            {
                EvaluationOrder.AppendTempName(writer, value);
                return;
            }

            using (writer.Parentheses())
            {
                EvaluationOrder.AppendTempName(writer, value);
                writer.Append($"&{binaryOperator.ShiftMask}");
            }
        }
    }

    // The C++ type of the target (an enum is its underlying type, as G# emits enum variables); decltype of the copy of
    // the target for a type that the dialect cannot spell (a type parameter), which is why such a target is copied
    private static void AppendTargetType(ExpressionSyntax left, int copy, ref TranspileContext context)
    {
        var type = context.Query.SemanticModel.GetTypeSymbol(left);
        if (NeedsDecltype(type) || type is null)
        {
            context.Emitter.Writer.Append("decltype");
            using (context.Emitter.Writer.Parentheses())
            {
                EvaluationOrder.AppendTempName(context.Emitter.Writer, copy);
            }

            return;
        }

        context.Emitter.Dialect.AppendType(ref context, type);
    }

    private static bool NeedsDecltype(ITypeSymbol? type) => type is null or ITypeParameterSymbol or { TypeKind: TypeKind.Error };

    // The binary operator of a compound assignment (+= gives +), or null for one that the sequence does not rewrite
    // (??= is handled before). >>>= is >> of an unsigned type; of int and long it is a Prelude call
    private static string? GetCompoundOperator(AssignmentExpressionSyntax node) => node.Kind() switch
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
