using System.Buffers;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles <c>Span&lt;T&gt;</c>, <c>ReadOnlySpan&lt;T&gt;</c>, <c>MemoryExtensions</c>, and <c>System.Array</c> write and
/// slice methods (<c>Fill</c>, <c>Clear</c>, <c>CopyTo</c>, <c>TryCopyTo</c>, <c>Reverse</c>, <c>Slice</c>, <c>AsSpan</c>,
/// <c>Array.Fill</c>, <c>Array.Clear</c>, <c>Array.Reverse</c>, <c>Array.Copy</c>) to <c>GSharpArray&lt;T&gt;</c> member
/// calls (#731), and reports any unmapped method on those four types as <c>GSH0003</c>.
/// </summary>
public sealed class SpanMemberTranspilerRule : IInvocationTranspilerRule
{
    private const string ReorderedArguments = "named arguments in another order than the parameters";

    private readonly record struct Operand(
        ExpressionSyntax Expression,
        bool IsSpanOrArray,
        ITypeSymbol? ValueType = null,
        bool RequiresRank1Array = false);

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        var kind = SpanWriteClassifier.ClassifyMethod(symbol);
        if (kind is SpanMemberKind.None or SpanMemberKind.Sort)
        {
            return false;
        }

        if (kind == SpanMemberKind.Unsupported)
        {
            FailUnsupported(node, symbol, ref context);
            return true;
        }

        var arguments = node.ArgumentList.Arguments;
        if (!IsInParameterOrder(arguments, symbol))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ReorderedArguments);
            context.Visitor.AppendUnsupportedPlaceholder(node);
            return true;
        }

        var operandBuffer = ArrayPool<Operand>.Shared.Rent(5);
        try
        {
            var operands = operandBuffer.AsSpan(0, 5);
            if (!TryBuildOperands(kind, node, symbol, operands, out var count))
            {
                FailUnsupported(node, symbol, ref context);
                return true;
            }

            var model = context.Query.SemanticModel;
            for (var i = 0; i < count; i++)
            {
                if (operands[i].RequiresRank1Array && model.GetTypeSymbol(operands[i].Expression) is not IArrayTypeSymbol { Rank: 1 })
                {
                    FailUnsupported(node, symbol, ref context);
                    return true;
                }
            }

            EmitCall(kind, operands[..count], ref context);
            return true;
        }
        finally
        {
            ArrayPool<Operand>.Shared.Return(operandBuffer, clearArray: true);
        }
    }

    private static void FailUnsupported(InvocationExpressionSyntax node, IMethodSymbol symbol, ref TranspileContext context)
    {
        context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, SpanWriteClassifier.FormatUnsupportedMethod(symbol));
        context.Visitor.AppendUnsupportedPlaceholder(node);
    }

    private static bool TryBuildOperands(
        SpanMemberKind kind,
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        Span<Operand> operands,
        out int count)
    {
        count = 0;
        var arguments = node.ArgumentList.Arguments;
        var hasReceiver = (!symbol.IsStatic || symbol.ReducedFrom != null) &&
                          node.Expression is MemberAccessExpressionSyntax;
        ExpressionSyntax? receiver = hasReceiver && node.Expression is MemberAccessExpressionSyntax access
            ? access.Expression
            : null;

        switch (kind)
        {
            case SpanMemberKind.Clear:
            case SpanMemberKind.Reverse:
            case SpanMemberKind.AsSpan0:
                if (receiver != null)
                {
                    if (arguments.Count != 0)
                    {
                        return false;
                    }

                    operands[0] = new Operand(receiver, IsSpanOrArray: true);
                }
                else
                {
                    if (arguments.Count != 1)
                    {
                        return false;
                    }

                    operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true);
                }

                count = 1;
                return true;

            case SpanMemberKind.Fill:
                if (receiver == null || arguments.Count != 1)
                {
                    return false;
                }

                operands[0] = new Operand(receiver, IsSpanOrArray: true);
                operands[1] = new Operand(arguments[0].Expression, IsSpanOrArray: false, symbol.Parameters[0].Type);
                count = 2;
                return true;

            case SpanMemberKind.CopyTo:
            case SpanMemberKind.TryCopyTo:
                if (receiver != null)
                {
                    if (arguments.Count != 1)
                    {
                        return false;
                    }

                    operands[0] = new Operand(receiver, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[0].Expression, IsSpanOrArray: true);
                }
                else
                {
                    if (arguments.Count != 2)
                    {
                        return false;
                    }

                    operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: true);
                }

                count = 2;
                return true;

            case SpanMemberKind.Slice1:
            case SpanMemberKind.AsSpan1:
                if (receiver != null)
                {
                    if (arguments.Count != 1)
                    {
                        return false;
                    }

                    operands[0] = new Operand(receiver, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[0].Expression, IsSpanOrArray: false, symbol.Parameters[0].Type);
                }
                else
                {
                    if (arguments.Count != 2)
                    {
                        return false;
                    }

                    operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                }

                count = 2;
                return true;

            case SpanMemberKind.Slice2:
            case SpanMemberKind.AsSpan2:
                if (receiver != null)
                {
                    if (arguments.Count != 2)
                    {
                        return false;
                    }

                    operands[0] = new Operand(receiver, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[0].Expression, IsSpanOrArray: false, symbol.Parameters[0].Type);
                    operands[2] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                }
                else
                {
                    if (arguments.Count != 3)
                    {
                        return false;
                    }

                    operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true);
                    operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                    operands[2] = new Operand(arguments[2].Expression, IsSpanOrArray: false, symbol.Parameters[2].Type);
                }

                count = 3;
                return true;

            case SpanMemberKind.ArrayFill2:
                if (arguments.Count != 2)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                count = 2;
                return true;

            case SpanMemberKind.ArrayFill4:
                if (arguments.Count != 4)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                operands[2] = new Operand(arguments[2].Expression, IsSpanOrArray: false, symbol.Parameters[2].Type);
                operands[3] = new Operand(arguments[3].Expression, IsSpanOrArray: false, symbol.Parameters[3].Type);
                count = 4;
                return true;

            case SpanMemberKind.ArrayClear1:
            case SpanMemberKind.ArrayReverse1:
                if (arguments.Count != 1)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                count = 1;
                return true;

            case SpanMemberKind.ArrayClear3:
            case SpanMemberKind.ArrayReverse3:
                if (arguments.Count != 3)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                operands[2] = new Operand(arguments[2].Expression, IsSpanOrArray: false, symbol.Parameters[2].Type);
                count = 3;
                return true;

            case SpanMemberKind.ArrayCopy3:
                if (arguments.Count != 3)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[2] = new Operand(arguments[2].Expression, IsSpanOrArray: false, symbol.Parameters[2].Type);
                count = 3;
                return true;

            case SpanMemberKind.ArrayCopy5:
                if (arguments.Count != 5)
                {
                    return false;
                }

                operands[0] = new Operand(arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[1] = new Operand(arguments[1].Expression, IsSpanOrArray: false, symbol.Parameters[1].Type);
                operands[2] = new Operand(arguments[2].Expression, IsSpanOrArray: true, RequiresRank1Array: true);
                operands[3] = new Operand(arguments[3].Expression, IsSpanOrArray: false, symbol.Parameters[3].Type);
                operands[4] = new Operand(arguments[4].Expression, IsSpanOrArray: false, symbol.Parameters[4].Type);
                count = 5;
                return true;

            default:
                return false;
        }
    }

    private static void EmitCall(SpanMemberKind kind, ReadOnlySpan<Operand> operands, ref TranspileContext context)
    {
        var count = operands.Length;
        var model = context.Query.SemanticModel;
        var writer = context.Emitter.Writer;

        Span<int> temps = stackalloc int[5];
        temps[..count].Fill(-1);

        var effects = ArrayPool<ExpressionEffects>.Shared.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
            {
                effects[i] = ExpressionEffects.Of(operands[i].Expression, model);
            }

            var last = count >= 2 ? EvaluationOrder.LastSpilledOperand(effects.AsSpan(0, count)) : -1;

            // Array.Fill(a, v, start, count) emits a.AsSpan(start, count).Fill(v), which reorders v after start/count.
            if (kind == SpanMemberKind.ArrayFill4 &&
                (!effects[1].Writes.IsEmpty || !effects[2].Writes.IsEmpty || !effects[3].Writes.IsEmpty))
            {
                last = Math.Max(last, 3);
            }

            // Array.Copy(..., length) emits length in both source and destination AsSpan(..., length) calls.
            var spillRepeatedLength = kind is SpanMemberKind.ArrayCopy3 or SpanMemberKind.ArrayCopy5 &&
                                      operands[count - 1].Expression is not (LiteralExpressionSyntax or IdentifierNameSyntax);
            if (spillRepeatedLength)
            {
                last = count - 1;
            }

            using var sequence = last >= 0 ? EvaluationOrder.BeginSequence(writer) : default;
            if (last >= 0)
            {
                for (var i = 0; i <= last; i++)
                {
                    if (effects[i].IsEmpty && !(spillRepeatedLength && i == count - 1))
                    {
                        continue;
                    }

                    if (operands[i].IsSpanOrArray)
                    {
                        temps[i] = DeclareSpanTemp(operands[i].Expression, ref context);
                    }
                    else
                    {
                        temps[i] = EvaluationOrder.DeclareTemp(
                            ref context,
                            operands[i].Expression,
                            reference: false,
                            operands[i].ValueType);
                    }
                }

                sequence.Return();
            }

            WriteCallShape(kind, operands, temps[..count], ref context);
        }
        finally
        {
            ArrayPool<ExpressionEffects>.Shared.Return(effects, clearArray: true);
        }
    }

    private static int DeclareSpanTemp(ExpressionSyntax expression, ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        writer.Append("auto");
        var temp = context.Emitter.SequenceTempCounter++;
        writer.Append(' ');
        EvaluationOrder.AppendTempName(writer, temp);
        writer.Append(" = ");
        StackArrayLocals.AppendView(expression, ref context);
        writer.AppendLine(";");
        return temp;
    }

    private static void WriteOperand(
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        int index,
        ref TranspileContext context)
    {
        if (temps[index] >= 0)
        {
            EvaluationOrder.AppendTempName(context.Emitter.Writer, temps[index]);
            return;
        }

        if (operands[index].IsSpanOrArray)
        {
            StackArrayLocals.AppendView(operands[index].Expression, ref context);
        }
        else
        {
            context.Visitor.Visit(operands[index].Expression);
        }
    }

    private static void WriteCallShape(
        SpanMemberKind kind,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        switch (kind)
        {
            case SpanMemberKind.Fill:
            case SpanMemberKind.ArrayFill2:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".Fill");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.Clear:
            case SpanMemberKind.ArrayClear1:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".Clear()");
                break;

            case SpanMemberKind.Reverse:
            case SpanMemberKind.ArrayReverse1:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".Reverse()");
                break;

            case SpanMemberKind.CopyTo:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".CopyTo");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.TryCopyTo:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".TryCopyTo");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.Slice1:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".Slice");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.Slice2:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".Slice");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 2, ref context);
                }

                break;

            case SpanMemberKind.AsSpan0:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan()");
                break;

            case SpanMemberKind.AsSpan1:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.AsSpan2:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 2, ref context);
                }

                break;

            case SpanMemberKind.ArrayFill4:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 2, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 3, ref context);
                }

                writer.Append(".Fill");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                }

                break;

            case SpanMemberKind.ArrayClear3:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 2, ref context);
                }

                writer.Append(".Clear()");
                break;

            case SpanMemberKind.ArrayReverse3:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 2, ref context);
                }

                writer.Append(".Reverse()");
                break;

            case SpanMemberKind.ArrayCopy3:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    writer.Append("0,");
                    WriteOperand(operands, temps, 2, ref context);
                }

                writer.Append(".CopyTo");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(".AsSpan");
                    using (writer.Parentheses())
                    {
                        writer.Append("0,");
                        WriteOperand(operands, temps, 2, ref context);
                    }
                }

                break;

            case SpanMemberKind.ArrayCopy5:
                WriteOperand(operands, temps, 0, ref context);
                writer.Append(".AsSpan");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 1, ref context);
                    writer.Append(',');
                    WriteOperand(operands, temps, 4, ref context);
                }

                writer.Append(".CopyTo");
                using (writer.Parentheses())
                {
                    WriteOperand(operands, temps, 2, ref context);
                    writer.Append(".AsSpan");
                    using (writer.Parentheses())
                    {
                        WriteOperand(operands, temps, 3, ref context);
                        writer.Append(',');
                        WriteOperand(operands, temps, 4, ref context);
                    }
                }

                break;
        }
    }

    private static bool IsInParameterOrder(SeparatedSyntaxList<ArgumentSyntax> arguments, IMethodSymbol method)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].NameColon != null &&
                (i >= method.Parameters.Length || !method.FindParameter(arguments, i).IsEqualTo(method.Parameters[i])))
            {
                return false;
            }
        }

        return true;
    }
}
