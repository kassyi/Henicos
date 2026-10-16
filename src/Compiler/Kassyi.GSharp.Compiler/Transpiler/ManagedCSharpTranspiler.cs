using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
internal static class ManagedCSharpTranspiler
{
#pragma warning disable RS0030
    public static string Generate(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        if (methodDecl.Body == null)
        {
            return string.Empty;
        }

        var tuple = AstLoweringPipeline.Lower(methodDecl.Body);
        var loweredBody = tuple.LoweredBody;
        var contextFields = tuple.ContextFields;

        var methodName = methodDecl.Identifier.Text;
        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        if (methodSymbol != null)
        {
            methodName = $"{methodSymbol.ContainingType.Name}_{methodName}";
        }

        using var writer = new SourceWriter();

        // Replace "_ctx->" with "_ctx." for C# managed execution
        var rewriter = new ManagedExecutionRewriter();
        loweredBody = (BlockSyntax)rewriter.Visit(loweredBody);

        var bodySource = loweredBody.NormalizeWhitespace().ToFullString().Replace("_ctx->", "_ctx.");

        var localInitBuilder = new StringBuilder();

        // Output context struct
        writer.AppendLine($"public class __gsharp_context_{methodName} {{");
        writer.AppendLine("public int _state;");
        writer.AppendLine("public uint _warpExchange;");
        writer.AppendLine("public bool _yieldedForGridSync;");
        if (contextFields != null)
        {
            foreach (var field in contextFields)
            {
                var typeSyntax = (field.Parent as VariableDeclarationSyntax)?.Type;
                if (typeSyntax == null)
                {
                    continue;
                }

                var typeString = typeSyntax.ToFullString().Trim();
                var initString = field.Initializer?.Value.ToFullString() ?? "";

                if (typeSyntax.IsVar || typeString == "var")
                {
                    try
                    {
                        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
                        if (typeInfo.Type != null && typeInfo.Type.TypeKind != TypeKind.Error)
                        {
                            typeString = typeInfo.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                        else
                        {
                            // Fallback: If we couldn't resolve the type but the initializer looks like a ref struct
                            if (initString.Contains("SharedMemory") || initString.Contains("Span"))
                            {
                                typeString = "var";
                            }
                            else
                            {
                                typeString = "dynamic";
                            }
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Syntax node is not within syntax tree
                        if (initString.Contains("SharedMemory") || initString.Contains("Span"))
                        {
                            typeString = "var";
                        }
                        else
                        {
                            typeString = "dynamic";
                        }
                    }
                }

                if (typeString.Contains("SharedMemory") || typeString.Contains("Span<") ||
                    initString.Contains("SharedMemory") || initString.Contains("Span"))
                {
                    // Un-hoist ref structs
                    bodySource = bodySource.Replace($"_ctx.{field.Identifier.ValueText}", field.Identifier.ValueText);

                    var init = field.Initializer != null ? field.Initializer.ToFullString() : "";
                    localInitBuilder.AppendLine(
                        $"        {(typeString == "int" ? "var" : typeString)} {field.Identifier.ValueText} {init};");

                    // Remove the assignment from the body to avoid double allocation (which consumes an extra Allocation Index)
                    if (!string.IsNullOrWhiteSpace(init))
                    {
                        bodySource = bodySource.Replace($"{field.Identifier.ValueText} {init};", "");
                        bodySource = bodySource.Replace($"{field.Identifier.ValueText}{init};", ""); // Without space
                    }
                }
                else
                {
                    writer.AppendLine($"public {typeString} {field.Identifier.ValueText};");
                }
            }
        }

        writer.AppendLine("}");
        writer.AppendLine();

        // Output the state machine function
        writer.Append($"public static unsafe void __gsharp_statemachine_{methodName}(");
        if (methodSymbol != null)
        {
            bool isFirst = true;
            foreach (var p in methodSymbol.Parameters)
            {
                if (!isFirst)
                {
                    writer.Append(", ");
                }

                isFirst = false;
                var refKind = p.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => ""
                };
                writer.Append($"{refKind}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}");
            }

            if (!isFirst)
            {
                writer.Append(", ");
            }

            writer.Append($"__gsharp_context_{methodName} _ctx");

            if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
            {
                writer.Append(
                    $", ref {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} __gsharp_auto_result");
            }
        }

        writer.AppendLine(") {");
        if (methodSymbol != null)
        {
            foreach (var p in methodSymbol.Parameters)
            {
                if (p.RefKind == RefKind.Out)
                {
                    writer.AppendLine($"        {p.Name} = default;");
                }
            }
        }

        writer.Append(localInitBuilder.ToString());
        writer.AppendLine(bodySource);
        writer.AppendLine("}");

        return writer.ToString();
    }

    private sealed class ManagedExecutionRewriter : CSharpSyntaxRewriter
    {
        private int _inLambdaOrLocalFuncCount;

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Identifier.Text == "_state" || node.Identifier.Text == "_warpExchange" ||
                node.Identifier.Text == "_yieldedForGridSync")
            {
                if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
                {
                    return base.VisitIdentifierName(node);
                }

                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("_ctx"),
                    node).WithTriviaFrom(node);
            }

            if (node.Identifier.Text == "BitOperations")
            {
                if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
                {
                    // It's the right side of a dot (e.g. System.Numerics.BitOperations), do not replace to avoid InvalidCastException
                    return base.VisitIdentifierName(node);
                }

                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("System.Numerics"),
                    SyntaxFactory.IdentifierName("BitOperations")).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitLocalFunctionStatement(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitSimpleLambdaExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitParenthesizedLambdaExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            _inLambdaOrLocalFuncCount++;
            var result = base.VisitAnonymousMethodExpression(node);
            _inLambdaOrLocalFuncCount--;
            return result;
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax inlinePtxAccess } &&
                inlinePtxAccess.Name.Identifier.Text == "InlinePtx")
            {
                return SyntaxFactory.ParseStatement(
                    "throw new global::System.NotSupportedException(\"Gpu.InlinePtx is not supported by the StandardCppDialect dialect\");")
                    .WithTriviaFrom(node);
            }

            if (node.Expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text != "GridStrideLoop" ||
                invocation.ArgumentList.Arguments.Count != 2)
            {
                return base.VisitExpressionStatement(node);
            }

            var countArg = invocation.ArgumentList.Arguments[0].Expression;
            if (invocation.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax { Block: not null } lambda)
            {
                return base.VisitExpressionStatement(node);
            }

            _inLambdaOrLocalFuncCount++;
            var loopVar = lambda.Parameter.Identifier.Text;

            var startExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX +" +
                " global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX");
            var stepExpr = SyntaxFactory.ParseExpression(
                "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX");
            var condition = SyntaxFactory.ParseExpression($"{loopVar} < ({countArg})");

            var declaration = SyntaxFactory.VariableDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.VariableDeclarator(lambda.Parameter.Identifier)
                        .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr))
                )
            );

            var incrementors = SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                SyntaxFactory.AssignmentExpression(
                    SyntaxKind.AddAssignmentExpression,
                    SyntaxFactory.IdentifierName(loopVar),
                    stepExpr
                )
            );

            var body = base.Visit(lambda.Block) as BlockSyntax ?? lambda.Block;
            _inLambdaOrLocalFuncCount--;

            return SyntaxFactory.ForStatement(
                declaration,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(),
                condition,
                incrementors,
                body
            ).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitReturnStatement(ReturnStatementSyntax node)
        {
            if (_inLambdaOrLocalFuncCount == 0 && !node.HasAnnotations("StateMachineReturn"))
            {
                var statements = new List<StatementSyntax>();

                if (node.Expression != null)
                {
                    statements.Add(SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName("__gsharp_auto_result"),
                            Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                        )
                    ));
                }

                statements.Add(SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.ParseExpression("_ctx._state"),
                        SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
                    )
                ));

                statements.Add(SyntaxFactory.ReturnStatement());

                return SyntaxFactory.Block(statements).WithTriviaFrom(node);
            }

            return base.VisitReturnStatement(node);
        }
    }
#pragma warning restore RS0030
}
