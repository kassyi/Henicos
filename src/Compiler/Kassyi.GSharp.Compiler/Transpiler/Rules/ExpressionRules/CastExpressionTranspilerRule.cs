using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles explicit C# type cast expressions into C-style parenthesized casts in target dialect code.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class CastExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<CastExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="CastExpressionTranspilerRule"/>.
    /// </summary>
    public CastExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(CastExpressionSyntax node, ref TranspileContext context)
    {
        // An integer constant is written as its value, as the binary operators write theirs: (int)(3L << 65) is 6 (#722)
        var operation = context.Query.SemanticModel.GetSafeOperation(node) is { } bound &&
                        bound.Syntax.RawKind == node.RawKind
            ? bound
            : null;
        if (IntegerSemanticsEmitter.TryAppendConstant(operation, node, ref context))
        {
            return;
        }

        // A float or double converted to an integer saturates and NaN gives 0, as .NET converts it; C++ leaves an
        // out-of-range conversion undefined (#724). A type narrower than int is the int result truncated:
        // (unsigned char)__gsharp_to_i32(x)
        if (operation is IConversionOperation conversion &&
            IntegerSemantics.GetConversionHelper(conversion, out var needsCast) is { } helper)
        {
            if (needsCast)
            {
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, context.Query.SemanticModel.GetTypeSymbol(node.Type));
                }
            }

            IntegerSemanticsEmitter.AppendUnaryCall(helper, node.Expression, ref context);
            return;
        }

        using (context.Emitter.Writer.Parentheses())
        {
            context.Emitter.Dialect.AppendType(ref context, context.Query.SemanticModel.GetTypeSymbol(node.Type));
        }

        context.Visitor.Visit(node.Expression);
    }
}
