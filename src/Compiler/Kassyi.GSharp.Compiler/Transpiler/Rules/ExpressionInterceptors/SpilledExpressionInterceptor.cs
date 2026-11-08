namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes the temporary of a part of a target that a step or compound assignment evaluated first
/// (<see cref="IntegerSemanticsEmitter"/>, <see cref="TranspileContext"/> <c>Emitter.SpilledExpressions</c>).
/// </summary>
internal sealed class SpilledExpressionInterceptor : IExpressionInterceptor
{
    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context)
    {
        var spilledExpressions = context.Emitter.SpilledExpressions;
        if (spilledExpressions.Count == 0 || !spilledExpressions.TryGetValue(expression, out var spilled))
        {
            return false;
        }

        EvaluationOrder.AppendTempName(context.Emitter.Writer, spilled);
        return true;
    }
}
