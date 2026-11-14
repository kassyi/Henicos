using Kassyi.GSharp.Compiler.Analysis;
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

            if (kind is SpanMemberKind.ArrayCopy3 or SpanMemberKind.ArrayCopy5)
            {
                var srcType = model.GetTypeSymbol(operands[0].Expression);
                var dstType = model.GetTypeSymbol(operands[kind == SpanMemberKind.ArrayCopy3 ? 1 : 2].Expression);
                if (!SpanWriteClassifier.AreCompatibleArrayCopyOperands(srcType, dstType))
                {
                    FailUnsupported(node, symbol, ref context);
                    return true;
                }
            }

            EmitCall(node, kind, operands[..count], ref context);
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

    private enum CallShape
    {
        None,
        MethodCall,
        SliceOrSpan,
        ArrayFill4,
        ArrayClear3,
        ArrayReverse3,
        ArrayCopy3,
        ArrayCopy5,
    }

    private enum SliceErrorCode
    {
        ArgumentOutOfRange,
        IndexOutOfRange,
        Argument,
    }

    private readonly record struct SpanMemberDescriptor(
        CallShape Shape,
        byte OperandCount,
        bool IsSystemArray,
        byte SecondarySpanIndex = 0,
        string? MemberName = null,
        SliceErrorCode ErrorCode = SliceErrorCode.ArgumentOutOfRange);

    private static SpanMemberDescriptor GetDescriptor(SpanMemberKind kind) => kind switch
    {
        SpanMemberKind.Clear => new(CallShape.MethodCall, 1, false, MemberName: "Clear"),
        SpanMemberKind.Reverse => new(CallShape.MethodCall, 1, false, MemberName: "Reverse"),
        SpanMemberKind.AsSpan0 => new(CallShape.MethodCall, 1, false, MemberName: "AsSpan"),
        SpanMemberKind.Fill => new(CallShape.MethodCall, 2, false, MemberName: "Fill"),
        SpanMemberKind.CopyTo => new(CallShape.MethodCall, 2, false, SecondarySpanIndex: 1, MemberName: "CopyTo"),
        SpanMemberKind.TryCopyTo => new(CallShape.MethodCall, 2, false, SecondarySpanIndex: 1, MemberName: "TryCopyTo"),
        SpanMemberKind.Slice1 => new(CallShape.SliceOrSpan, 2, false, MemberName: "Slice"),
        SpanMemberKind.AsSpan1 => new(CallShape.SliceOrSpan, 2, false, MemberName: "AsSpan"),
        SpanMemberKind.Slice2 => new(CallShape.SliceOrSpan, 3, false, MemberName: "Slice"),
        SpanMemberKind.AsSpan2 => new(CallShape.SliceOrSpan, 3, false, MemberName: "AsSpan"),
        SpanMemberKind.ArrayFill2 => new(CallShape.MethodCall, 2, true, MemberName: "Fill"),
        SpanMemberKind.ArrayFill4 => new(CallShape.ArrayFill4, 4, true),
        SpanMemberKind.ArrayClear1 => new(CallShape.MethodCall, 1, true, MemberName: "Clear"),
        SpanMemberKind.ArrayClear3 => new(CallShape.ArrayClear3, 3, true, ErrorCode: SliceErrorCode.IndexOutOfRange),
        SpanMemberKind.ArrayReverse1 => new(CallShape.MethodCall, 1, true, MemberName: "Reverse"),
        SpanMemberKind.ArrayReverse3 => new(CallShape.ArrayReverse3, 3, true),
        SpanMemberKind.ArrayCopy3 => new(CallShape.ArrayCopy3, 3, true, SecondarySpanIndex: 1, ErrorCode: SliceErrorCode.Argument),
        SpanMemberKind.ArrayCopy5 => new(CallShape.ArrayCopy5, 5, true, SecondarySpanIndex: 2, ErrorCode: SliceErrorCode.Argument),
        _ => default,
    };

    // The operands in the order of the descriptor: the receiver first when the call has one (an instance or reduced
    // extension call), then the arguments. A System.Array call is static, so it has no receiver, and its arrays must
    // have rank 1
    private static bool TryBuildOperands(
        SpanMemberKind kind,
        InvocationExpressionSyntax node,
        IMethodSymbol symbol,
        Span<Operand> operands,
        out int count)
    {
        count = 0;
        var descriptor = GetDescriptor(kind);
        var arguments = node.ArgumentList.Arguments;
        var receiver = (!symbol.IsStatic || symbol.ReducedFrom != null) &&
                       node.Expression is MemberAccessExpressionSyntax access
            ? access.Expression
            : null;
        var offset = receiver is null ? 0 : 1;
        if (descriptor.OperandCount == 0 || arguments.Count != descriptor.OperandCount - offset)
        {
            return false;
        }

        var rank1 = descriptor.IsSystemArray;
        operands[0] = new Operand(receiver ?? arguments[0].Expression, IsSpanOrArray: true, RequiresRank1Array: rank1);
        for (var i = 1; i < descriptor.OperandCount; i++)
        {
            var isSpan = i == descriptor.SecondarySpanIndex;
            operands[i] = new Operand(
                arguments[i - offset].Expression,
                IsSpanOrArray: isSpan,
                ValueType: isSpan ? null : symbol.Parameters[i - offset].Type,
                RequiresRank1Array: rank1 && isSpan
            );
        }

        count = descriptor.OperandCount;
        return true;
    }

    private static void EmitCall(InvocationExpressionSyntax node, SpanMemberKind kind, ReadOnlySpan<Operand> operands, ref TranspileContext context)
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

            last = Math.Max(last, LastSystemArrayWrite(kind, effects.AsSpan(1, count - 1)));

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

            WriteCallShape(node, kind, operands, temps[..count], ref context);
        }
        finally
        {
            ArrayPool<ExpressionEffects>.Shared.Return(effects, clearArray: true);
        }
    }

    // A System.Array call checks its arrays for null before it runs (WriteArrayOperand), after C# has evaluated every
    // argument: the index of the last operand after the first that writes, which is then evaluated first, into a
    // temporary (-1 for another call, or when none writes)
    private static int LastSystemArrayWrite(SpanMemberKind kind, ReadOnlySpan<ExpressionEffects> rest)
    {
        if (!GetDescriptor(kind).IsSystemArray)
        {
            return -1;
        }

        for (var i = rest.Length - 1; i >= 0; i--)
        {
            if (!rest[i].Writes.IsEmpty)
            {
                return i + 1;
            }
        }

        return -1;
    }

    // __gsharp_array_argument(<array>), or __gsharp_copy_arguments(<source>, <destination>) for the source of
    // Array.Copy: the array operand of a System.Array call, checked for null (ArgumentNullException) before the call
    // reads it. The destination of Array.Copy is written a second time for the check; an operand with an effect is a
    // temporary
    private static void WriteArrayOperand(
        SpanMemberKind kind,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        var destination = kind switch
        {
            SpanMemberKind.ArrayCopy3 => 1,
            SpanMemberKind.ArrayCopy5 => 2,
            _ => -1,
        };
        writer.Append(destination < 0
            ? GSharpInternalNames.Prelude.ArrayArgument
            : GSharpInternalNames.Prelude.CopyArguments);
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 0, ref context);
            if (destination >= 0)
            {
                writer.Append(',');
                WriteOperand(operands, temps, destination, ref context);
            }
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
        InvocationExpressionSyntax node,
        SpanMemberKind kind,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var descriptor = GetDescriptor(kind);
        switch (descriptor.Shape)
        {
            case CallShape.MethodCall:
                WriteMethodCall(kind, descriptor, operands, temps, ref context);
                break;
            case CallShape.SliceOrSpan:
                WriteSliceOrSpan(descriptor.MemberName!, descriptor.ErrorCode, node, operands, temps, ref context);
                break;
            case CallShape.ArrayFill4:
                WriteArrayFill4(node, operands, temps, ref context);
                break;
            case CallShape.ArrayClear3:
                WriteArrayClear3(node, operands, temps, ref context);
                break;
            case CallShape.ArrayReverse3:
                WriteArrayReverse3(node, operands, temps, ref context);
                break;
            case CallShape.ArrayCopy3:
                WriteArrayCopy3(node, operands, temps, ref context);
                break;
            case CallShape.ArrayCopy5:
                WriteArrayCopy5(node, operands, temps, ref context);
                break;
        }
    }

    private static void WriteMethodCall(
        SpanMemberKind kind,
        SpanMemberDescriptor descriptor,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        if (descriptor.IsSystemArray)
        {
            WriteArrayOperand(kind, operands, temps, ref context);
        }
        else
        {
            WriteOperand(operands, temps, 0, ref context);
        }

        writer.Append('.');
        // The member name of a MethodCall descriptor is always set (GetDescriptor)
        writer.Append(descriptor.MemberName!);
        if (operands.Length > 1)
        {
            using (writer.Parentheses())
            {
                WriteOperand(operands, temps, 1, ref context);
            }
        }
        else
        {
            writer.Append("()");
        }
    }

    private static string FormatRangeMessage(SliceErrorCode errorCode, string place) => errorCode switch
    {
        SliceErrorCode.IndexOutOfRange => $"G#: index was outside the bounds of the array at {place}",
        SliceErrorCode.Argument => $"G#: offset and length were out of bounds for the array at {place}",
        _ => $"G#: index was out of range at {place}",
    };

    private static void AppendRangeMessage(
        SourceWriter writer,
        SliceErrorCode errorCode,
        InvocationExpressionSyntax node,
        ref TranspileContext context)
    {
        var place = SourcePlace.Format(context.Query.SemanticModel, node);
        var msg = FormatRangeMessage(errorCode, place);
        writer.Append(", ");
        CppStringLiteral.Append(writer, msg);
    }

    // , <start site>u[, <count site>u]: the error sites of the failed checks of a call (the messages name the parameter
    // C# names); in a method without an error site table the messages themselves, which the mapper knows
    private static void AppendRangeChecks(
        SourceWriter writer,
        InvocationExpressionSyntax node,
        string startMessage,
        string startKind,
        string? countMessage,
        string? countKind,
        ref TranspileContext context)
    {
        var table = context.Emitter.ErrorSites;
        var model = context.Query.SemanticModel;
        var member = context.Emitter.CurrentMemberName ?? "";
        writer.Append(", ");
        if (table is null)
        {
            CppStringLiteral.Append(writer, startMessage);
            if (countMessage is not null)
            {
                writer.Append(", ");
                CppStringLiteral.Append(writer, countMessage);
            }

            return;
        }

        writer.Append($"{table.Register(node, model, member, startMessage, startKind)}u");
        if (countMessage is not null)
        {
            writer.Append($", {table.Register(node, model, member, countMessage, countKind ?? "")}u");
        }
    }

    private static void WriteSliceOrSpan(
        string memberName,
        SliceErrorCode errorCode,
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteOperand(operands, temps, 0, ref context);
        writer.Append('.');
        writer.Append(memberName);
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
            if (operands.Length > 2)
            {
                writer.Append(',');
                WriteOperand(operands, temps, 2, ref context);
            }

            AppendRangeMessage(writer, errorCode, node, ref context);
        }
    }

    private static void WriteArrayFill4(
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteArrayOperand(SpanMemberKind.ArrayFill4, operands, temps, ref context);
        writer.Append(".AsSpan");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 2, ref context);
            writer.Append(',');
            WriteOperand(operands, temps, 3, ref context);
            AppendRangeChecks(
                writer,
                node,
                ArgumentExceptionMessages.FillStartIndex,
                "Array.Fill.StartIndex",
                ArgumentExceptionMessages.FillCount,
                "Array.Fill.Count",
                ref context);
        }

        writer.Append(".Fill");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
        }
    }

    private static void WriteArrayClear3(
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteArrayOperand(SpanMemberKind.ArrayClear3, operands, temps, ref context);
        writer.Append(".AsSpan<GSharpErrorCode::IndexOutOfRange>");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
            writer.Append(',');
            WriteOperand(operands, temps, 2, ref context);
            AppendRangeMessage(writer, SliceErrorCode.IndexOutOfRange, node, ref context);
        }

        writer.Append(".Clear()");
    }

    private static void WriteArrayReverse3(
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteArrayOperand(SpanMemberKind.ArrayReverse3, operands, temps, ref context);
        writer.Append(".AsSpan");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
            writer.Append(',');
            WriteOperand(operands, temps, 2, ref context);
            AppendRangeMessage(writer, SliceErrorCode.ArgumentOutOfRange, node, ref context);
        }

        writer.Append(".Reverse()");
    }

    private static void WriteArrayCopy3(
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteArrayOperand(SpanMemberKind.ArrayCopy3, operands, temps, ref context);
        writer.Append(".AsSpan<GSharpErrorCode::Argument>");
        using (writer.Parentheses())
        {
            writer.Append("0,");
            WriteOperand(operands, temps, 2, ref context);
            AppendRangeChecks(
                writer,
                node,
                ArgumentExceptionMessages.CopySource,
                "Array.Copy.Source",
                null,
                null,
                ref context);
        }

        writer.Append(".CopyTo");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
            writer.Append(".AsSpan<GSharpErrorCode::Argument>");
            using (writer.Parentheses())
            {
                writer.Append("0,");
                WriteOperand(operands, temps, 2, ref context);
                AppendRangeChecks(
                    writer,
                    node,
                    ArgumentExceptionMessages.CopyDestination,
                    "Array.Copy.Destination",
                    null,
                    null,
                    ref context);
            }
        }
    }

    private static void WriteArrayCopy5(
        InvocationExpressionSyntax node,
        ReadOnlySpan<Operand> operands,
        ReadOnlySpan<int> temps,
        ref TranspileContext context)
    {
        var writer = context.Emitter.Writer;
        WriteArrayOperand(SpanMemberKind.ArrayCopy5, operands, temps, ref context);
        writer.Append(".AsSpan<GSharpErrorCode::Argument>");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 1, ref context);
            writer.Append(',');
            WriteOperand(operands, temps, 4, ref context);
            AppendRangeChecks(
                writer,
                node,
                ArgumentExceptionMessages.CopySource,
                "Array.Copy.Source",
                null,
                null,
                ref context);
        }

        writer.Append(".CopyTo");
        using (writer.Parentheses())
        {
            WriteOperand(operands, temps, 2, ref context);
            writer.Append(".AsSpan<GSharpErrorCode::Argument>");
            using (writer.Parentheses())
            {
                WriteOperand(operands, temps, 3, ref context);
                writer.Append(',');
                WriteOperand(operands, temps, 4, ref context);
                AppendRangeChecks(
                    writer,
                    node,
                    ArgumentExceptionMessages.CopyDestination,
                    "Array.Copy.Destination",
                    null,
                    null,
                    ref context);
            }
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
