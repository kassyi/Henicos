using Kassyi.GSharp.Compiler.Lowering;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Transpiles a C# method declaration into a pure C# managed state machine and wrapper for host-side debugging.
/// </summary>
/// <remarks>
/// Types named from symbols go through <see cref="SymbolExtensions.AppendCsharpTypeName"/>; syntax nodes become text only in <see cref="ToSource"/>.
/// </remarks>
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

        var (localInits, regularFields, unhoistedSymbols) =
            ProcessContextFields(contextFields, semanticModel, methodName);

        var unhoistedBody = new UnhoistRefStructRewriter(unhoistedSymbols, semanticModel, methodName).Visit(loweredBody) as BlockSyntax
                            ?? throw new InvalidOperationException($"Unhoisting ref-struct fields removed the body of kernel '{methodName}'.");
        var bodySource = RewriteAndNormalizeBody(unhoistedBody);

        using var writer = new SourceWriter();
        EmitContextClass(writer, methodName, regularFields);
        EmitStateMachineMethod(writer, methodName, methodSymbol, localInits, bodySource);
        return writer.ToString();
    }

#pragma warning disable RS0030 // Final emission of a node; the text is not parsed, compared, or edited afterwards
    private static string ToSource(SyntaxNode node) => node.NormalizeWhitespace().ToFullString();
#pragma warning restore RS0030

    private static string RewriteAndNormalizeBody(BlockSyntax loweredBody)
    {
        var rewriter = new ManagedExecutionRewriter();
        var rewritten = rewriter.Visit(loweredBody) as BlockSyntax
                        ?? throw new InvalidOperationException("Managed execution rewriting removed the kernel body.");
        return ToSource(rewritten);
    }

    /// <summary>A hoisted context field with the type that <see cref="HoistedFieldTypeResolver"/> resolved for it.</summary>
    private readonly record struct ContextField(VariableDeclaratorSyntax Declarator, TypeSyntax DeclaredType, ITypeSymbol ResolvedType)
    {
        public string Name => Declarator.Identifier.ValueText.ToCSharpIdentifier();
    }

    private static (ImmutableArray<ContextField> LocalInits, ImmutableArray<ContextField> RegularFields, HashSet<ILocalSymbol> UnhoistedSymbols)
        ProcessContextFields(
            IReadOnlyList<VariableDeclaratorSyntax>? contextFields,
            SemanticModel semanticModel,
            string methodName)
    {
        var regularFields = ImmutableArray.CreateBuilder<ContextField>();
        var localInits = ImmutableArray.CreateBuilder<ContextField>();
        var unhoistedSymbols = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        if (contextFields == null)
        {
            return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedSymbols);
        }

        foreach (var field in contextFields)
        {
            if (field.Parent is not VariableDeclarationSyntax declaration)
            {
                continue;
            }

            var contextField = new ContextField(field, declaration.Type,
                HoistedFieldTypeResolver.Resolve(field, semanticModel, methodName));

            if (contextField.ResolvedType.IsRefLikeType)
            {
                if (semanticModel.GetDeclaredSymbol<ILocalSymbol>(field) is { } localSymbol)
                {
                    unhoistedSymbols.Add(localSymbol);
                }

                localInits.Add(contextField);
            }
            else
            {
                regularFields.Add(contextField);
            }
        }

        return (localInits.ToImmutable(), regularFields.ToImmutable(), unhoistedSymbols);
    }

    /// <summary>Writes the field type: a written type as written, <c>var</c> as the resolved type.</summary>
    private static void AppendFieldType(SourceWriter writer, ContextField field)
    {
        if (field.DeclaredType.IsVar)
        {
            writer.AppendCsharpTypeName(field.ResolvedType);
        }
        else
        {
            // Without trivia: a comment above the original declaration would otherwise land inside the emitted one
            writer.Append(ToSource(field.DeclaredType.WithoutTrivia()));
        }
    }

    private static void EmitContextClass(
        SourceWriter writer,
        string methodName,
        ImmutableArray<ContextField> regularFields)
    {
        writer.AppendLine($"public class __gsharp_context_{methodName} {{");
        writer.AppendLine($"public int {GSharpInternalNames.State};");
        writer.AppendLine($"public uint {GSharpInternalNames.WarpExchange};");
        writer.AppendLine($"public bool {GSharpInternalNames.YieldedForGridSync};");

        foreach (var field in regularFields)
        {
            writer.Append("public ");
            AppendFieldType(writer, field);
            writer.AppendLine($" {field.Name};");
        }

        writer.AppendLine("}");
        writer.AppendLine();
    }

    private static void EmitStateMachineMethod(
        SourceWriter writer,
        string methodName,
        IMethodSymbol? methodSymbol,
        ImmutableArray<ContextField> localInits,
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
                    writer.AppendLine($"        {p.Name.ToCSharpIdentifier()} = default;");
                }
            }
        }

        foreach (var localInit in localInits)
        {
            writer.Append("        ");
            AppendFieldType(writer, localInit);
            writer.Append($" {localInit.Name}");
            if (localInit.Declarator.Initializer is { } initializer)
            {
                writer.Append($" {ToSource(initializer.WithoutTrivia())}");
            }

            writer.AppendLine(";");
        }

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

        foreach (var p in methodSymbol.Parameters)
        {
            writer.Append(p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => ""
            });
            writer.AppendCsharpTypeName(p.Type);
            writer.Append($" {p.Name.ToCSharpIdentifier()}, ");
        }

        writer.Append($"__gsharp_context_{methodName} {GSharpInternalNames.Context}");

        if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.Append(", ref ");
            writer.AppendCsharpTypeName(methodSymbol.ReturnType);
            writer.Append(" __gsharp_auto_result");
        }
    }

    /// <summary>Matches <c>__gsharp_ctx-&gt;X</c>, the context access that <see cref="ContextHoister"/> emits.</summary>
    private static bool IsContextPointerAccess(SyntaxNode? node) =>
        node is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: GSharpInternalNames.Context } } memberAccess &&
        memberAccess.IsKind(SyntaxKind.PointerMemberAccessExpression);

    private sealed class UnhoistRefStructRewriter(
        HashSet<ILocalSymbol> unhoistedSymbols,
        SemanticModel semanticModel,
        string kernelName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = base.VisitMemberAccessExpression(node);
            if (!IsContextPointerAccess(visited) || visited is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            var symbol = semanticModel.GetSymbol(memberAccess.Name) as ILocalSymbol ??
                         semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
            return symbol != null && unhoistedSymbols.Contains(symbol)
                ? memberAccess.Name.WithTriviaFrom(memberAccess)
                : memberAccess;
        }

        public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax { Left: MemberAccessExpressionSyntax memberAccess } assignment &&
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                IsContextPointerAccess(memberAccess))
            {
                // Only the declarator-derived initialization resolves through GetDeclaredSymbol; later user assignments stay
                var declaredSymbol = semanticModel.GetDeclaredSymbol<ILocalSymbol>(memberAccess.Name);
                if (declaredSymbol != null && unhoistedSymbols.Contains(declaredSymbol))
                {
                    if (node.Parent is BlockSyntax or SwitchSectionSyntax)
                    {
                        return null;
                    }

#pragma warning disable RS0030 // Statement text for a human-readable internal-error report only
                    throw new InvalidOperationException(
                        $"Cannot remove ref-struct initialization statement in kernel '{kernelName}' for local '{declaredSymbol.Name}': statement '{node.ToFullString().Trim()}' is not inside a block or switch section.");
#pragma warning restore RS0030
                }
            }

            return base.VisitExpressionStatement(node);
        }
    }

    private sealed class ManagedExecutionRewriter : CSharpSyntaxRewriter
    {
        private int _inLambdaOrLocalFuncCount;

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = base.VisitMemberAccessExpression(node);
            if (!IsContextPointerAccess(visited) || visited is not MemberAccessExpressionSyntax memberAccess)
            {
                return visited;
            }

            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                memberAccess.Expression,
                memberAccess.Name).WithTriviaFrom(memberAccess);
        }

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
            // Visit the count so that context accesses in it are rewritten like the rest of the body
            var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;
            var condition = SyntaxFactory.BinaryExpression(
                SyntaxKind.LessThanExpression,
                SyntaxFactory.IdentifierName(loopVar),
                SyntaxFactory.ParenthesizedExpression(countExpr));

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

            var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);

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
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(GSharpInternalNames.Context),
                        SyntaxFactory.IdentifierName(GSharpInternalNames.State)),
                    SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(-1))
                )
            ));

            statements.Add(SyntaxFactory.ReturnStatement());

            return SyntaxFactory.Block(statements).WithTriviaFrom(node);
        }
    }
}
