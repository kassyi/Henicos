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
    /// <inheritdoc />
    public override void Transpile(ObjectCreationExpressionSyntax node, ref TranspileContext context) =>
        ObjectCreationHelper.TranspileObjectCreation(node, ref context);
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
    /// <inheritdoc />
    public override void Transpile(ImplicitObjectCreationExpressionSyntax node, ref TranspileContext context) =>
        ObjectCreationHelper.TranspileObjectCreation(node, ref context);
}

/// <summary>
/// Internal helper facilitating shared struct instantiation and brace-enclosed initialization logic.
/// </summary>
internal static class ObjectCreationHelper
{
    public static void TranspileObjectCreation(BaseObjectCreationExpressionSyntax node, ref TranspileContext context)
    {
        var typeSymbol = context.Query.SemanticModel.GetTypeSymbol(node);
        if (typeSymbol is null or IErrorTypeSymbol || typeSymbol.TypeKind == TypeKind.Error)
        {
            return;
        }

        var initializer = node.Initializer;
        var argumentList = node.ArgumentList;

        if (typeSymbol.IsSharedMemoryType())
        {
            if (typeSymbol is not INamedTypeSymbol namedType || namedType.TypeArguments.Length != 1)
            {
                return;
            }

            var typeArg = namedType.TypeArguments[0];
            // [WHY] One buffer per declaration per block, like a CUDA __shared__ variable (see SharedMemorySiteKey)
            var siteKey = SharedMemorySiteKey.ToCppLiteral(SharedMemorySiteKey.Compute(context.Query.SemanticModel, node));

            using (context.Emitter.Writer.Braces())
            {
                context.Emitter.Writer.Append(" (");
                context.Emitter.Dialect.AppendType(ref context, typeArg);
                context.Emitter.Writer.Append($"*){GSharpInternalNames.Prelude.AllocateSharedMemory}({siteKey}, ");

                using (context.Emitter.Writer.Parentheses())
                {
                    if (argumentList?.Arguments.Count > 0)
                    {
                        context.Visitor.Visit(argumentList.Arguments[0].Expression);
                    }
                    else
                    {
                        context.Emitter.Writer.Append("1");
                    }
                }

                context.Emitter.Writer.Append(" * sizeof");
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, typeArg);
                }

                context.Emitter.Writer.Append(", alignof");
                using (context.Emitter.Writer.Parentheses())
                {
                    context.Emitter.Dialect.AppendType(ref context, typeArg);
                }

                context.Emitter.Writer.Append(")");
                context.Emitter.Writer.Append(" ");
            }
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
            context.Emitter.Writer.Append("[&]() ");
            using (context.Emitter.Writer.Scope())
            {
                context.Emitter.Dialect.AppendType(ref context, typeSymbol);
                context.Emitter.Writer.Append(' ').Append(GSharpInternalNames.Lowering.AggregateTemp);
                TranspileArgumentListAsBraces(argumentList, ref context);

                context.Emitter.Writer.Append(';');
                context.Emitter.Writer.Append(' ');

                foreach (var expr in initializer.Expressions)
                {
                    if (expr is not AssignmentExpressionSyntax assignment)
                    {
                        continue;
                    }

                    context.Emitter.Writer.Append($"{GSharpInternalNames.Lowering.AggregateTemp}.");
                    context.Visitor.Visit(assignment.Left);
                    context.Emitter.Writer.Append(" = ");
                    context.Visitor.Visit(assignment.Right);
                    context.Emitter.Writer.Append("; ");
                }

                context.Emitter.Writer.Append($"return {GSharpInternalNames.Lowering.AggregateTemp}; ");
            }

            context.Emitter.Writer.Append("()");
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
                            .Append(", ");
                    }

                    context.Visitor.Visit(arg);
                    first = false;
                }
            }
        }
        else
        {
            context.Emitter.Writer.Append("{}");
        }
    }
}
