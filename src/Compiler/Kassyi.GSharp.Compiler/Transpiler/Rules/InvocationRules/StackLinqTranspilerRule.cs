namespace Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules;

/// <summary>
/// Transpiles Kassyi.ZeroAlloc.Linq method invocations into their corresponding C++ Prelude equivalents.
/// </summary>
public sealed class StackLinqTranspilerRule : IInvocationTranspilerRule
{
    public const string ReorderedArguments =
        "StackLinq call with reordered named arguments whose evaluations interfere";

    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return false;
        }

        if (StackLinqHelper.HasInterferingReorderedArguments(node, symbol, context.Query.SemanticModel))
        {
            context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, ReorderedArguments);
            return true;
        }

        var methodName = symbol.Name;

        // Detect indexed overloads and map to WhereAt / SelectAt / CountAt
        if (methodName is "Where" or "Select" or "Count")
        {
            var lastParam = symbol.Parameters.Length > 0 ? symbol.Parameters[^1] : null;
            if (lastParam?.Type is INamedTypeSymbol { DelegateInvokeMethod.Parameters.Length: 2 })
            {
                methodName += "At";
            }
        }

        // Output the C++ namespace for our StackLinq implementation
        context.Emitter.Writer.Append("Kassyi::ZeroAlloc::Linq::StackLinq::");
        context.Emitter.Writer.Append(methodName);

        using (context.Emitter.Writer.Parentheses())
        {
            if (StackLinqHelper.TryGetSourceExpression(node, symbol, out var sourceExpr))
            {
                // The source template deduces GSharpArray<T>, which a stack array does not convert to
                StackArrayLocals.AppendView(sourceExpr, ref context);
            }

            var stageArgCount = StackLinqHelper.GetStageArgumentCount(symbol);
            for (var i = 0; i < stageArgCount; i++)
            {
                if (!StackLinqHelper.TryGetStageArgument(node, symbol, i, out var stageArg))
                {
                    continue;
                }

                context.Emitter.Writer.Append(',');
                if (StackArrayLocals.IsStackArrayReference(stageArg.Expression, context.Query.SemanticModel))
                {
                    StackArrayLocals.AppendView(stageArg.Expression, ref context);
                }
                else
                {
                    context.Visitor.Visit(stageArg);
                }
            }
        }

        return true;
    }
}
