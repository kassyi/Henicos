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
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node);
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
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node);
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
        if (typeSymbol is null or IErrorTypeSymbol || typeSymbol.TypeKind == TypeKind.Error)
        {
            return;
        }

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

        context.Reporter.Report(GSharpDiagnostics.ObjectCreationNotSupported, node);
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
        context.Emitter.Dialect.AppendType(ref context, typeSymbol);
        TranspileArgumentListAsBraces(argumentList, ref context);
    }

    private static void TranspileStructCreationWithInitializer(ITypeSymbol typeSymbol,
        InitializerExpressionSyntax initializer, ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        using (context.Emitter.Writer.Parentheses())
        {
            using (context.Emitter.Writer.Brackets())
            {
                context.Emitter.Writer.Append("&");
            }

            context.Emitter.Writer.AppendParentheses();
            context.Emitter.Writer.AppendSpace();
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                context.Emitter.Writer.Append(" _tmp");
                TranspileArgumentListAsBraces(argumentList, ref context);

                context.Emitter.Writer.AppendSemiColon();
                context.Emitter.Writer.AppendSpace();

                foreach (var expr in initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assignment)
                    {
                        continue;
                    }

                    context.Emitter.Writer.Append("_tmp.");
                    context.Visitor.Visit(assignment.Left);
                    context.Emitter.Writer.Append(" = ");
                    context.Visitor.Visit(assignment.Right);
                    context.Emitter.Writer.Append("; ");
                }

                context.Emitter.Writer.Append("return _tmp; ");
            }

            context.Emitter.Writer.AppendParentheses();
        }
    }

    private static void TranspileArgumentListAsBraces(ArgumentListSyntax? argumentList, ref TranspileContext context)
    {
        if (argumentList is { Arguments.Count: > 0 })
        {
            using (context.Emitter.Writer.Braces())
            {
                var first = true;
                foreach (var arg in argumentList.Arguments)
                {
                    if (!first)
                    {
                        context.Emitter.Writer
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
            context.Emitter.Writer.AppendEmptyScope();
        }
    }
}
