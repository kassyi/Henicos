namespace Kassyi.GSharp.Compiler.Transpiler.Rules;

/// <summary>Rule contract for intercepting and transpiling specific method invocations in GPU kernels.</summary>
internal interface IInvocationTranspilerRule
{
    bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol);
}
