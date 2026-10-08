namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles Kassyi.ZeroAlloc.Linq method invocations into their corresponding C++ Prelude equivalents.
/// </summary>
public sealed class StackLinqTranspilerRule : IInvocationTranspilerRule
{
    public bool TryTranspile(InvocationExpressionSyntax node, ref TranspileContext context, IMethodSymbol symbol)
    {
        if (symbol.ContainingNamespace?.Name != "Linq" || 
            symbol.ContainingNamespace?.ContainingNamespace?.Name != "ZeroAlloc" ||
            symbol.ContainingNamespace?.ContainingNamespace?.ContainingNamespace?.Name != "Kassyi")
        {
            return false;
        }

        // Output the C++ namespace for our StackLinq implementation
        context.Emitter.Writer.Append("Kassyi::ZeroAlloc::Linq::StackLinq::");
        context.Emitter.Writer.Append(symbol.Name);

        using (context.Emitter.Writer.Parentheses())
        {
            var arguments = node.ArgumentList.Arguments;

            // If this is an extension method call via instance.Method(), the instance is the first argument
            if (symbol.IsExtensionMethod && node.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                context.Visitor.Visit(memberAccess.Expression);
                if (arguments.Count > 0)
                {
                    context.Emitter.Writer.AppendComma();
                }
            }
            else if (symbol.IsExtensionMethod)
            {
                 // Called as a normal static method, first arg is the instance, which is handled normally below
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
