using static Kassyi.GSharp.Compiler.GSharpInternalNames.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal sealed class AverageStrategy : IStackLinqTerminalStrategy
{
    public void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage)
    {
        context.Emitter.Writer.AppendLine($"double {resultVar} = 0;");
        context.Emitter.Writer.AppendLine($"int {resultVar}{CountSuffix} = 0;");
    }

    public void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex)
    {
        StackLinqHelper.EmitAccumulateElement(ref context, in stage, resultVar, valIndex);
        context.Emitter.Writer.AppendLine($"{resultVar}{CountSuffix}++;");
    }

    public void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar)
    {
        context.Emitter.Writer.AppendLine($"if ({resultVar}{CountSuffix} == 0)");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine(
                $"{GSharpInternalNames.Prelude.Trap}(" +
                "GSharpErrorCode::InvalidOperation, \"Sequence contains no elements\");");
        }
        context.Emitter.Writer.AppendLine("else");
        using (context.Emitter.Writer.Braces())
        {
            context.Emitter.Writer.AppendLine($"{resultVar} = {resultVar} / (double)({resultVar}{CountSuffix});");
        }
    }
}
