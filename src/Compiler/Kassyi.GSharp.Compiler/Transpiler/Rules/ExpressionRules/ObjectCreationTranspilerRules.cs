namespace Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

/// <summary>
/// Transpiles explicit object creation expressions (<c>new MyStruct(...)</c>) into target dialect C++ struct constructor calls or brace initializers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ObjectCreationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ObjectCreationExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ObjectCreationExpressionTranspilerRule"/>.
    /// </summary>
    public ObjectCreationExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ObjectCreationExpressionSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        ObjectCreationHelper.TryTranspileObjectCreationHelper(typeSymbol, node, node.Initializer, node.ArgumentList,
            ref context);
    }
}

/// <summary>
/// Transpiles target-typed implicit object creation expressions (<c>new(...)</c>) into target dialect C++ struct initializers.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b> This rule is stateless and thread-safe.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment).</para>
/// </remarks>
public sealed class ImplicitObjectCreationExpressionTranspilerRule : SyntaxNodeTranspilerRuleBase<ImplicitObjectCreationExpressionSyntax>
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ImplicitObjectCreationExpressionTranspilerRule"/>.
    /// </summary>
    public ImplicitObjectCreationExpressionTranspilerRule()
    {
    }

    /// <inheritdoc />
    public override void Transpile(ImplicitObjectCreationExpressionSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.SemanticModel.GetTypeInfo(node).Type;
        ObjectCreationHelper.TryTranspileObjectCreationHelper(typeSymbol, node, node.Initializer, node.ArgumentList,
            ref context);
    }
}

/// <summary>
/// Internal helper facilitating shared struct instantiation and brace-enclosed initialization logic.
/// </summary>
internal static class ObjectCreationHelper
{
    public static void TryTranspileObjectCreationHelper(
        ITypeSymbol? typeSymbol,
        ExpressionSyntax node,
        InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        if (typeSymbol is { Name: TypeConstants.SharedMemoryType } &&
            typeSymbol.ContainingNamespace.IsCoreNamespace())
        {
            context.Visitor.DefaultVisit(node);
            return;
        }

        if (typeSymbol is { TypeKind: TypeKind.Struct })
        {
            TranspileStructCreationCore(typeSymbol, initializer, argumentList, ref context);
            return;
        }

        context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.ObjectCreationNotSupported,
            node.GetLocation()));
        context.Visitor.DefaultVisit(node);
    }

    private static void TranspileStructCreationCore(ITypeSymbol typeSymbol, InitializerExpressionSyntax? initializer,
        ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        if (initializer != null)
        {
            TranspileStructCreationWithInitializer(typeSymbol, initializer, argumentList, ref context);
        }
        else
        {
            TranspileStructCreationWithoutInitializer(typeSymbol, argumentList, ref context);
        }
    }

    private static void TranspileStructCreationWithoutInitializer(ITypeSymbol typeSymbol,
        ArgumentListSyntax? argumentList,
        ref TranspileContext context)
    {
        context.AppendType(typeSymbol);
        TranspileArgumentListAsBraces(argumentList, ref context);
    }

    private static void TranspileStructCreationWithInitializer(ITypeSymbol typeSymbol,
        InitializerExpressionSyntax initializer, ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        using (context.Writer.Parentheses())
        {
            using (context.Writer.Brackets())
            {
                context.Writer.Append("&");
            }

            context.Writer.AppendParentheses();
            context.Writer.AppendSpace();
            using (context.Writer.Scope())
            {
                context.AppendType(typeSymbol);
                context.Writer.Append(" _tmp");
                TranspileArgumentListAsBraces(argumentList, ref context);

                context.Writer.AppendSemiColon();
                context.Writer.AppendSpace();

                foreach (var expr in initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assignment)
                    {
                        continue;
                    }

                    context.Writer.Append("_tmp.");
                    context.Visitor.Visit(assignment.Left);
                    context.Writer.Append(" = ");
                    context.Visitor.Visit(assignment.Right);
                    context.Writer.Append("; ");
                }

                context.Writer.Append("return _tmp; ");
            }

            context.Writer.AppendParentheses();
        }
    }

    private static void TranspileArgumentListAsBraces(ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Writer.Braces())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Writer
                            .AppendComma()
                            .AppendSpace();
                    }

                    context.Visitor.Visit(arg);
                    first = false;
                }
            }
        }
        else
        {
            context.Writer.AppendEmptyScope();
        }
    }
}
