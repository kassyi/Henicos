namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;

internal interface IStackLinqIntermediateStrategy
{
    void EmitInitialValue(ref TranspileContext context, in LinqStage stage, int stageIndex);
    void Emit(ref TranspileContext context, in LinqStage stage, int stageIndex, ref int valIndex, ref int openBraces);
}
