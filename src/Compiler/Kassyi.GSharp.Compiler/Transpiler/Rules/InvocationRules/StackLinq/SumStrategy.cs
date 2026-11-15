using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>
/// Terminal strategy for <c>Sum</c>. A <c>float</c> sum adds in a <c>double</c> accumulator, as System.Linq does; the
/// fused loop rounds it to <c>float</c> once at the end (<see cref="AccumulatesInDouble"/>). An integer sum adds the
/// elements exactly, in 64 bits (<c>int</c>, <c>uint</c>) or in 128 bits (<c>long</c>, <c>ulong</c>), and range-checks
/// the total once at the end (<see cref="GetNarrowFunction"/>), so a sum that does not fit throws
/// <see cref="OverflowException"/> and the value does not depend on the order of the additions.
/// </summary>
internal sealed class SumStrategy : IStackLinqTerminalStrategy
{
    public static bool AccumulatesInDouble(in LinqStage stage) =>
        stage.Symbol.ReturnType.SpecialType == SpecialType.System_Single;

    /// <summary>
    /// The prelude function that range-checks the exact sum of an integer <c>Sum</c> and narrows it to the result type,
    /// or <see langword="null"/> for a sum that adds in its own type.
    /// </summary>
    public static string? GetNarrowFunction(in LinqStage stage) => stage.Symbol.ReturnType.SpecialType switch
    {
        SpecialType.System_Int32 => NarrowSumInt32,
        SpecialType.System_UInt32 => NarrowSumUInt32,
        SpecialType.System_Int64 => NarrowSumInt64,
        SpecialType.System_UInt64 => NarrowSumUInt64,
        _ => null
    };

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        switch (stage.Symbol.ReturnType.SpecialType)
        {
            case SpecialType.System_Int32:
                context.Emitter.Writer.AppendLine($"long long {resultVar} = 0;");
                return;
            case SpecialType.System_UInt32:
                context.Emitter.Writer.AppendLine($"unsigned long long {resultVar} = 0;");
                return;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
                context.Emitter.Writer.AppendLine($"{WideSum} {resultVar} = {{0, 0}};");
                return;
        }

        if (AccumulatesInDouble(in stage))
        {
            context.Emitter.Writer.Append("double");
        }
        else
        {
            context.Emitter.Dialect.AppendType(ref context, stage.Symbol.ReturnType);
        }

        context.Emitter.Writer.AppendLine($" {resultVar} = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        switch (stage.Symbol.ReturnType.SpecialType)
        {
            case SpecialType.System_Int64:
                StackLinqHelper.EmitWideAccumulateElement(ref context, in stage, resultVar, valIndex, WideAddInt64);
                break;
            case SpecialType.System_UInt64:
                StackLinqHelper.EmitWideAccumulateElement(ref context, in stage, resultVar, valIndex, WideAddUInt64);
                break;
            default:
                StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);
                break;
        }
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}
