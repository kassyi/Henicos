using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules.StackLinq;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles Kassyi.ZeroAlloc.Linq method invocations into their corresponding C++ Prelude equivalents.
/// </summary>
public sealed class StackLinqTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (!StackLinqHelper.IsStackLinqMethod(symbol))
        {
            return false;
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
            var arguments = node.ArgumentList.Arguments;
            switch (symbol.IsExtensionMethod)
            {
                // If this is an extension method call via instance.Method(), the instance is the first argument
                case true when node.Expression is MemberAccessExpressionSyntax memberAccess:
                {
                    context.Visitor.Visit(memberAccess.Expression);

                    if (arguments.Count > 0)
                    {
                        context.Emitter.Writer.AppendComma();
                    }

                    break;
                }
                case true:
                    // Called as a normal static method, first arg is the instance, which is handled normally below
                    break;
            }

            for (var i = 0; i < arguments.Count; i++)
            {
                context.Visitor.Visit(arguments[i]);

                if (i < arguments.Count - 1)
                {
                    context.Emitter.Writer.AppendComma();
                }
            }
        }

        return true;
    }
}
