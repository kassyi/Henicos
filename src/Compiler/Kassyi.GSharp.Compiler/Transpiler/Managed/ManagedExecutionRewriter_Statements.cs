namespace Kassyi.GSharp.Compiler.Transpiler;

internal sealed partial class ManagedExecutionRewriter
{
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

        // for (int i = <start>; i < (<count>); i += <step>) <body>
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(loopVar),
            SyntaxFactory.ParenthesizedExpression(countExpr)
        );

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
        if (isDeviceFunction && _inLambdaOrLocalFuncCount == 0)
        {
            return RewriteDeviceFunctionReturn(node);
        }

        if (_inLambdaOrLocalFuncCount != 0 ||
            node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
        {
            return base.VisitReturnStatement(node);
        }

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);

        // { [__gsharp_auto_result = <value>;] __gsharp_ctx.__gsharp_state = -1; return; }
        if (node.Expression != null)
        {
            statements.Add(
                CreateAssignment(
                    SyntaxFactory.IdentifierName(GSharpInternalNames.ReturnValue.Buffer),
                    Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                )
            );
        }

        statements.Add(CreateContextStateAssignment(-1));
        statements.Add(SyntaxFactory.ReturnStatement());

        return SyntaxFactory.Block(statements).WithTriviaFrom(node);
    }

    // A suspension reports "not completed"; a return of the user's code writes the result, resets the frame for the
    // next call and reports completion
    private SyntaxNode RewriteDeviceFunctionReturn(ReturnStatementSyntax node)
    {
        if (node.HasAnnotations(LoweringAnnotations.StateMachineReturnAnnotationKind))
        {
            // return false;
            return SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                .WithTriviaFrom(node);
        }

        // { [__gsharp_result = <value>;] __gsharp_ctx.__gsharp_state = 0; return true; }
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(3);
        if (node.Expression != null)
        {
            statements.Add(
                CreateAssignment(
                    SyntaxFactory.IdentifierName(GSharpInternalNames.SuspendingCall.Result),
                    Visit(node.Expression) as ExpressionSyntax ?? node.Expression
                )
            );
        }

        statements.Add(CreateContextStateAssignment(0));
        statements.Add(
            SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));

        return SyntaxFactory.Block(statements).WithTriviaFrom(node);
    }

    // <target> = <value>;
    private static ExpressionStatementSyntax CreateAssignment(ExpressionSyntax target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value)
        );

    // __gsharp_ctx.__gsharp_state = <state>;
    private static ExpressionStatementSyntax CreateContextStateAssignment(int state) =>
        CreateAssignment(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.Context),
                SyntaxFactory.IdentifierName(GSharpInternalNames.StateMachine.State)
            ),
            SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(state))
        );
}
