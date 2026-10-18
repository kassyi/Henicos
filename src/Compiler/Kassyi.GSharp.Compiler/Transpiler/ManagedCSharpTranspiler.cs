using Kassyi.GSharp.Compiler.Lowering;

#pragma warning disable RS0030 // ManagedCSharpTranspiler is a host-side debug C#-to-C# state machine rewriter

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
internal static class ManagedCSharpTranspiler
{
    public static string Generate(MethodDeclarationSyntax methodDecl, SemanticModel semanticModel)
    {
        if (methodDecl.Body == null)
        {
            return string.Empty;
        }

        var tuple = AstLoweringPipeline.Lower(methodDecl.Body);
        var loweredBody = tuple.LoweredBody;
        var contextFields = tuple.ContextFields;

        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        var methodName = MethodSignatureTranspiler.GetKernelMethodName(methodDecl, methodSymbol);

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
                    var resolvedType = typeSyntax.SyntaxTree == semanticModel.SyntaxTree
                        ? semanticModel.GetTypeInfo(typeSyntax).Type
                        : null;
                    if (resolvedType != null && resolvedType.TypeKind != TypeKind.Error)
                    {
                        typeString = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    else
                    {
                        // Fallback: If we couldn't resolve the type (e.g., synthesized node) but the initializer looks like a ref struct
                        typeString = initString.Contains("SharedMemory") || initString.Contains("Span")
                            ? "var"
                            : "dynamic";
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
            var isFirst = true;
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
                writer.Append($", ref {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} __gsharp_auto_result");
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
            if (node.Parent is MemberAccessExpressionSyntax parent && parent.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Identifier.Text is "_state" or "_warpExchange" or "_yieldedForGridSync")
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("_ctx"),
                    node).WithTriviaFrom(node);
            }

            if (node.Identifier.Text == "BitOperations")
            {
                return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("System.Numerics"),
                    SyntaxFactory.IdentifierName("BitOperations")).WithTriviaFrom(node);
            }

            return base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) =>
            VisitNestedCallable(node, base.VisitLocalFunctionStatement);

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitSimpleLambdaExpression);

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitParenthesizedLambdaExpression);

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) =>
            VisitNestedCallable(node, base.VisitAnonymousMethodExpression);

        private SyntaxNode? VisitNestedCallable<TNode>(TNode node, Func<TNode, SyntaxNode?> baseVisitor)
        {
            _inLambdaOrLocalFuncCount++;
            try
            {
                return baseVisitor(node);
            }
            finally
            {
                _inLambdaOrLocalFuncCount--;
            }
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
}
