using Kassyi.GSharp.Compiler.Lowering;

#pragma warning disable RS0030 // ManagedCSharpTranspiler is a host-side debug C#-to-C# state machine rewriter

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
internal static class ManagedCSharpTranspiler
{
    public static string Generate(
        MethodDeclarationSyntax methodDecl,
        SemanticModel semanticModel,
        (BlockSyntax LoweredBody, IReadOnlyList<VariableDeclaratorSyntax> ContextFields)? preLowered = null)
    {
        if (methodDecl.Body == null)
        {
            return string.Empty;
        }

        var (loweredBody, contextFields) = preLowered ?? AstLoweringPipeline.Lower(methodDecl.Body);
        var methodSymbol = semanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDecl);
        var methodName = MethodSignatureTranspiler.GetKernelMethodName(methodDecl, methodSymbol);

        var bodySource = RewriteAndNormalizeBody(loweredBody);
        var (transformedBody, localInits, regularFields) =
            ProcessContextFields(bodySource, contextFields, semanticModel);

        using var writer = new SourceWriter();
        EmitContextClass(writer, methodName, regularFields);
        EmitStateMachineMethod(writer, methodName, methodSymbol, localInits, transformedBody);
        return writer.ToString();
    }

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody)
    {
        var rewriter = new ManagedExecutionRewriter();
        var rewritten = (BlockSyntax)rewriter.Visit(loweredBody);
        return rewritten.NormalizeWhitespace().ToFullString()
            .Replace($"{GSharpInternalNames.Context}->", $"{GSharpInternalNames.Context}.");
    }

    private static (string TransformedBody, string LocalInits, List<(string Type, string Name)> RegularFields)
        ProcessContextFields(
            string bodySource,
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel)
    {
        var regularFields = new List<(string Type, string Name)>();
        var localInitBuilder = new StringBuilder();

        if (contextFields == null)
        {
            return (bodySource, string.Empty, regularFields);
        }

        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
            {
                continue;
            }

            var typeSyntax = declaration.Type;
            var initString = field.Initializer?.Value.ToFullString() ?? "";
            var typeString = ResolveFieldType(typeSyntax, initString, semanticModel);
            var fieldName = field.Identifier.ValueText;

            if (IsRefStructType(typeString, initString))
            {
                bodySource = UnhoistRefStructField(bodySource, fieldName, typeString, field.Initializer,
                    localInitBuilder);
            }
            else
            {
                regularFields.Add((typeString, fieldName));
            }
        }

        return (bodySource, localInitBuilder.ToString(), regularFields);
    }

    private static string ResolveFieldType(
        TypeSyntax typeSyntax,
        string initString,
        SemanticModel semanticModel)
    {
        var typeString = typeSyntax.ToFullString().Trim();
        if (!typeSyntax.IsVar && typeString != "var")
        {
            return typeString;
        }

        var resolvedType = typeSyntax.SyntaxTree == semanticModel.SyntaxTree
            ? semanticModel.GetTypeInfo(typeSyntax).Type
            : null;

        if (resolvedType != null && resolvedType.TypeKind != TypeKind.Error)
        {
            return resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        // Fallback: If we couldn't resolve the type (e.g., synthesized node) but the initializer looks like a ref struct
        return initString.Contains(TypeConstants.SharedMemoryType) || initString.Contains(TypeConstants.SpanType)
            ? "var"
            : "dynamic";
    }

    private static bool IsRefStructType(string typeString, string initString) =>
        typeString.Contains(TypeConstants.SharedMemoryType) || typeString.Contains("Span<") ||
        initString.Contains(TypeConstants.SharedMemoryType) || initString.Contains(TypeConstants.SpanType);

    private static string UnhoistRefStructField(
        string bodySource,
        string fieldName,
        string typeString,
        EqualsValueClauseSyntax? initializer,
        StringBuilder localInitBuilder)
    {
        bodySource = bodySource.Replace($"{GSharpInternalNames.Context}.{fieldName}", fieldName);

        var init = initializer != null ? initializer.ToFullString() : "";
        localInitBuilder.AppendLine(
            $"        {(typeString == "int" ? "var" : typeString)} {fieldName} {init};");

        if (string.IsNullOrWhiteSpace(init))
        {
            return bodySource;
        }

        bodySource = bodySource.Replace($"{fieldName} {init};", "");
        bodySource = bodySource.Replace($"{fieldName}{init};", "");

        return bodySource;
    }

    private static void EmitContextClass(
        SourceWriter writer,
        string methodName,
        IReadOnlyList<(string Type, string Name)> regularFields)
    {
        writer.AppendLine($"public class __gsharp_context_{methodName} {{");
        writer.AppendLine($"public int {GSharpInternalNames.State};");
        writer.AppendLine($"public uint {GSharpInternalNames.WarpExchange};");
        writer.AppendLine($"public bool {GSharpInternalNames.YieldedForGridSync};");

        foreach (var (type, name) in regularFields)
        {
            writer.AppendLine($"public {type} {name};");
        }

        writer.AppendLine("}");
        writer.AppendLine();
    }

    private static void EmitStateMachineMethod(
        SourceWriter writer,
        string methodName,
        IMethodSymbol? methodSymbol,
        string localInits,
        string bodySource)
    {
        EmitStateMachineSignature(writer, methodName, methodSymbol);
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

        writer.Append(localInits);
        writer.AppendLine(bodySource);
        writer.AppendLine("}");
    }

    private static void EmitStateMachineSignature(
        SourceWriter writer,
        string methodName,
        IMethodSymbol? methodSymbol)
    {
        writer.Append($"public static unsafe void __gsharp_statemachine_{methodName}(");
        if (methodSymbol == null)
        {
            return;
        }

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

        writer.Append($"__gsharp_context_{methodName} {GSharpInternalNames.Context}");

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.Append(
                $", ref {methodSymbol.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} __gsharp_auto_result");
        }
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

            return node.Identifier.Text switch
            {
                GSharpInternalNames.State or GSharpInternalNames.WarpExchange or GSharpInternalNames.YieldedForGridSync =>
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName(GSharpInternalNames.Context), node)
                        .WithTriviaFrom(node),
                TypeConstants.BitOperationsType => SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName("System.Numerics"), SyntaxFactory.IdentifierName(TypeConstants.BitOperationsType))
                    .WithTriviaFrom(node),
                _ => base.VisitIdentifierName(node)
            };
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
            if (node.Expression is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: TypeConstants.GpuMacros.InlinePtx }
                })
            {
                return SyntaxFactory.ParseStatement(
                        "throw new global::System.NotSupportedException(\"Gpu.InlinePtx is not supported by the StandardCppDialect dialect\");")
                    .WithTriviaFrom(node);
            }

            if (node.Expression is not InvocationExpressionSyntax invocation ||
                invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                memberAccess.Name.Identifier.Text != TypeConstants.GpuMacros.GridStrideLoop ||
                invocation.ArgumentList.Arguments.Count != 2)
            {
                return base.VisitExpressionStatement(node);
            }

            var countArg = invocation.ArgumentList.Arguments[0].Expression;
            if (invocation.ArgumentList.Arguments[1].Expression is not SimpleLambdaExpressionSyntax
                {
                    Block: not null
                } lambda)
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
            if (_inLambdaOrLocalFuncCount != 0 || node.HasAnnotations("StateMachineReturn"))
            {
                return base.VisitReturnStatement(node);
            }

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
                    SyntaxFactory.ParseExpression($"{GSharpInternalNames.Context}.{GSharpInternalNames.State}"),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
                )
            ));

            statements.Add(SyntaxFactory.ReturnStatement());

            return SyntaxFactory.Block(statements).WithTriviaFrom(node);
        }
    }
}
