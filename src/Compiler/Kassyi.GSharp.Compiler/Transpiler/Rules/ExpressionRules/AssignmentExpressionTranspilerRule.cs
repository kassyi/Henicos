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

    /// <summary>The GSH0003 text for a <c>??=</c> whose target is a computed property.</summary>
    public const string CoalesceAssignmentToAccessorProperty = "??= of a computed property";

    /// <inheritdoc />
    public override void Transpile(AssignmentExpressionSyntax node, ref TranspileContext context)
    {
        if (DeconstructionAssignment.IsDeconstruction(node))
        {
            DeconstructionAssignment.Transpile(node, ref context);
            return;
        }

        var isAccessorProperty = PropertyAccessorCall.IsAccessorPropertyTarget(node.Left, ref context);
        if (node.IsKind(SyntaxKind.CoalesceAssignmentExpression))
        {
            if (isAccessorProperty)
            {
                context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, CoalesceAssignmentToAccessorProperty);
                context.Visitor.AppendUnsupportedPlaceholder(node);
                return;
            }

            NullRepresentation.AppendCoalesceAssignment(node.Left, node.Right, ref context);
            return;
        }

        // A discard target keeps only the evaluation of the right side (#1289)
        if (context.Query.SemanticModel.IsDiscard(node.Left))
        {
            DeconstructionAssignment.AppendDiscardedValue(node, ref context);
            return;
        }

        if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            if (!isAccessorProperty &&
                node.Right is not RefExpressionSyntax &&
                OperandsInterfere(node, readsTarget: false, ref context, out _))
            {
                TranspileSequencedAssignment(node, ref context);
                return;
            }

            PropertyAccessorCall.TranspileAssignment(node, node.OperatorToken.Text, ref context);
            return;
        }

        // A user-defined compound operator has no C++ operator unless it is a macro, which its own rule expands
        // (GSH0060)
        if (CallTargetEmitter.TryFailOperator(node, ref context))
        {
            return;
        }

        if (isAccessorProperty)
        {
            PropertyAccessorAssignment.TranspileAssignment(node, ref context);
            return;
        }

        // int and long arithmetic that C++ leaves undefined goes through the Prelude, a shift that stays a C++ operator
        // masks its count (#722), and a floating-point remainder is fmod (#727)
        var compound = context.Query.SemanticModel.GetSafeOperation(node) is ICompoundAssignmentOperation operation &&
                       operation.Syntax.RawKind == node.RawKind
            ? operation
            : null;
        var compoundOperator = CompoundOperator.ForAssignment(node, compound);
        var helper = compoundOperator?.Helper;
        var shiftMask = compoundOperator?.ShiftMask ?? 0;

        // C++ has no >>>=; the integer types are handled here (nint and nuint are not)
        if (node.IsKind(SyntaxKind.UnsignedRightShiftAssignmentExpression) && helper == null && shiftMask == 0)
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, node.Kind().ToString());
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return;
        }

        // A floating-point remainder whose value has another type than the target converts it first (fmod has no device
        // overload for two types)
        if (compoundOperator is { } sequencedOperator &&
            (OperandsInterfere(node, readsTarget: true, ref context, out var rightEffects) ||
             (sequencedOperator.ConvertsValue && compound?.Target.Type is { } targetType &&
              ArithmeticConversions.IsConverted(compound.Value, targetType))))
        {
            TranspileSequencedCompoundAssignment(node, sequencedOperator, rightEffects, ref context);
            return;
        }

        if (compound != null && helper != null)
        {
            IntegerSemanticsEmitter.AppendCompound(compound, node, helper, ref context);
            return;
        }

        context.Visitor.Visit(node.Left);
        if (shiftMask != 0)
        {
            // x >>>= n of an unsigned type is x >>= n
            context.Emitter.Writer.Append(node.IsKind(SyntaxKind.LeftShiftAssignmentExpression) ? "<<=" : ">>=");
            IntegerSemanticsEmitter.AppendShiftCount(compound?.Value, node.Right, shiftMask, ref context);
            return;
        }

        context.Emitter.Writer.Append(node.OperatorToken.Text);
        context.Visitor.Visit(node.Right);
    }

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
    // The operation is <helper>(__gsharp_seq_1,__gsharp_seq_2) for int and long arithmetic (#722) and a floating-point
    // remainder (fmodf, #727), and a shift masks its count: __gsharp_seq_1>>(__gsharp_seq_2&31) (#722)
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
        var value = EvaluationOrder.DeclareTemp(ref context, node.Right, reference: false,
            binaryOperator.ConvertsValue ? model.GetTypeSymbol(node.Left) : null);
        sequence.Return();
        EvaluationOrder.AppendTempName(writer, target);
        writer.Append(" = ");
        using (writer.Parentheses())
        {
            AppendTargetType(node.Left, current, ref context);
        }

        binaryOperator.AppendOperation(writer, current, value);
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
}
