using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

/// <summary>
/// Terminal strategy for <c>Sum</c>. A <c>float</c> sum adds in a <c>double</c> accumulator, as System.Linq does; the
/// fused loop rounds it to <c>float</c> once at the end (<see cref="AccumulatesInDouble"/>).
/// </summary>
internal sealed class SumStrategy : IStackLinqTerminalStrategy
{
    public static bool AccumulatesInDouble(in LinqStage stage) =>
        stage.Symbol.ReturnType.SpecialType == SpecialType.System_Single;

    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
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

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex) =>
        StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar) { }
}
