namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules.StackLinq;

internal interface IStackLinqTerminalStrategy
{
    void EmitInitialValue(ref TranspileContext context, string resultVar, in LinqStage stage);
    void Emit(ref TranspileContext context, in LinqStage stage, string resultVar, int valIndex);
    void EmitPostLoop(ref TranspileContext context, in LinqStage stage, string resultVar);
}
