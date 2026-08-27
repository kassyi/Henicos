namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

internal interface IInvocationTranspilerRule
{
    bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol);
}
