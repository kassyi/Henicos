namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionInterceptors;

/// <summary>
/// Writes an expression that C# converts implicitly with a user-defined struct conversion operator as a call of that
/// operator (<see cref="UserDefinedConversionHelper.TryAppendImplicitConversion"/>).
/// </summary>
internal sealed class ImplicitConversionInterceptor : IExpressionInterceptor
{
    /// <inheritdoc />
    public bool TryAppend(ExpressionSyntax expression, ref TranspileContext context) =>
        UserDefinedConversionHelper.TryAppendImplicitConversion(expression, ref context);
}
