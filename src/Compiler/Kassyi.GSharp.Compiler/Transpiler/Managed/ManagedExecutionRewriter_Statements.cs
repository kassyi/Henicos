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
            !TryClassifyLoopMacro(invocation, out var call))
        {
            return base.VisitExpressionStatement(node);
        }

        var loop = call.Kind == GpuMacroKind.ForUnroll
            ? RewriteForUnroll(invocation, call)
            : RewriteGridStrideLoop(invocation, call);
        return loop?.WithTriviaFrom(node) ?? base.VisitExpressionStatement(node);
    }

    // The macro as the semantic model resolves it. A call that does not resolve is left alone, as in
    // PreLoweringExpander: the call is then not guessed from its name, and the unrewritten macro fails where it runs
    private bool TryClassifyLoopMacro(InvocationExpressionSyntax invocation, out GpuMacroCall call)
    {
        call = semanticModel.GetSymbol(invocation) is IMethodSymbol method
            ? GpuMacroClassifier.Classify(method)
            : default;
        return call.Kind is GpuMacroKind.ForUnroll or GpuMacroKind.GridStrideLoop;
    }

    // Gpu.GridStrideLoop([ref ctx,] count, body) becomes:
    // for (int __gsharp_unroll_<n> = <block dim> * <block idx> + <thread idx>; __gsharp_unroll_<n> < (<count>);
    //      __gsharp_unroll_<n> += <block dim> * <grid dim>) <body>
    // The loop variable has a name of its own, like the one of ForUnroll: the lambda parameter may be named like a local
    // of the kernel, which the loop would otherwise hide (CS0136)
    private ForStatementSyntax? RewriteGridStrideLoop(InvocationExpressionSyntax invocation, GpuMacroCall call)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (!TryGetLoopMacroBody(arguments, call, out var lambda, out var loopParameter))
        {
            return null;
        }

        var indexName = NextLoopVariableName();
        var startExpr = SyntaxFactory.ParseExpression(
            "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX +" +
            " global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX");
        var stepExpr = SyntaxFactory.ParseExpression(
            "global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX * global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX");
        // Visit the count so that context accesses in it are rewritten like the rest of the body
        var countArg = arguments[call.ArgumentOffset].Expression;
        var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;

        // <index> < (<count>)
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            SyntaxFactory.ParenthesizedExpression(countExpr)
        );

        var declaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(indexName))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr))
            )
        );

        var incrementors = SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.AddAssignmentExpression,
                SyntaxFactory.IdentifierName(indexName),
                stepExpr
            )
        );

        var substituter = CreateSubstituter(arguments, lambda, loopParameter, indexName);
        var body = VisitLoopBody(lambda, substituter);

        return SyntaxFactory.ForStatement(
            declaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            incrementors,
            body
        );
    }

    // Gpu.ForUnroll([ref ctx,] start, count, body) becomes:
    // for (int __gsharp_unroll_<n> = <start>; __gsharp_unroll_<n> < unchecked(<start> + <count>);
    //      __gsharp_unroll_<n>++) <body>
    // The loop variable has a name of its own, so that it never meets a local that the lambda parameter is named like.
    // The sum is unchecked, as in the C# method and in the C++ loop: a sum beyond int.MaxValue wraps around and the
    // loop runs no iteration, where a constant sum would not compile
    private ForStatementSyntax? RewriteForUnroll(InvocationExpressionSyntax invocation, GpuMacroCall call)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (!TryGetLoopMacroBody(arguments, call, out var lambda, out var loopParameter))
        {
            return null;
        }

        var startArg = arguments[call.ArgumentOffset].Expression.WithoutTrivia();
        var countArg = arguments[call.ArgumentOffset + 1].Expression.WithoutTrivia();
        var startExpr = Visit(startArg) as ExpressionSyntax ?? startArg;
        var countExpr = Visit(countArg) as ExpressionSyntax ?? countArg;
        var indexName = NextLoopVariableName();

        var substituter = CreateSubstituter(arguments, lambda, loopParameter, indexName);
        var body = VisitLoopBody(lambda, substituter);

        var declaration = SyntaxFactory.VariableDeclaration(
            SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(indexName))
                    .WithInitializer(SyntaxFactory.EqualsValueClause(startExpr))
            )
        );

        // <index> < unchecked(<start> + <count>)
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(indexName),
            SyntaxFactory.CheckedExpression(
                SyntaxKind.UncheckedExpression,
                SyntaxFactory.BinaryExpression(
                    SyntaxKind.AddExpression,
                    Parenthesize(startArg, startExpr),
                    Parenthesize(countArg, countExpr)
                )
            )
        );

        // <index>++
        var incrementor = SyntaxFactory.PostfixUnaryExpression(
            SyntaxKind.PostIncrementExpression,
            SyntaxFactory.IdentifierName(indexName)
        );

        return SyntaxFactory.ForStatement(
            declaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            body
        );
    }

    // The numbers continue from the loops that PreLoweringExpander made, so that every loop variable of the kernel
    // differs
    private string NextLoopVariableName() => $"{GSharpInternalNames.Lowering.UnrollIndexPrefix}{_nextUnrollId++}";

    private static bool TryGetLoopMacroBody(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        GpuMacroCall call,
        [NotNullWhen(true)] out LambdaExpressionSyntax? lambda,
        [NotNullWhen(true)] out ParameterSyntax? loopParameter)
    {
        loopParameter = null;
        lambda = null;
        if (arguments.Count <= call.LambdaArgumentIndex ||
            arguments[call.LambdaArgumentIndex].Expression is not LambdaExpressionSyntax bodyLambda ||
            GpuCodeConstraints.GetMacroLoopParameter(bodyLambda, call.HasContext) is not { } parameter ||
            parameter.Identifier.ValueText.Length == 0)
        {
            return false;
        }

        lambda = bodyLambda;
        loopParameter = parameter;
        return true;
    }

    // Replaces the loop parameter by <loopName> and, for the ref ctx overloads, the context parameter by the context
    // argument
    private MacroLambdaSubstituter CreateSubstituter(
        SeparatedSyntaxList<ArgumentSyntax> arguments,
        LambdaExpressionSyntax lambda,
        ParameterSyntax loopParameter,
        string loopName)
    {
        ParameterSyntax? contextParameter = null;
        ExpressionSyntax? contextArgument = null;
        if (lambda is ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 2 } parenthesized &&
            arguments.Count > 0)
        {
            contextParameter = parenthesized.ParameterList.Parameters[0];
            contextArgument = arguments[0].Expression.WithoutTrivia();
        }

        return new MacroLambdaSubstituter(semanticModel, loopParameter, loopName, contextParameter, contextArgument);
    }

    // The body of the macro lambda as a block. A return in it is not rewritten as the kernel's return
    private BlockSyntax VisitLoopBody(LambdaExpressionSyntax lambda, MacroLambdaSubstituter? substituter)
    {
        var substituted = substituter is null ? lambda.Body : substituter.Visit(lambda.Body) ?? lambda.Body;
        var block = substituted as BlockSyntax ??
                    SyntaxFactory.Block(SyntaxFactory.ExpressionStatement((ExpressionSyntax)substituted));

        _inLambdaOrLocalFuncCount++;
        try
        {
            return Visit(block) as BlockSyntax ?? block;
        }
        finally
        {
            _inLambdaOrLocalFuncCount--;
        }
    }

    // A compound operand keeps its grouping inside the sum. The operand as written decides: a name that the rewrite
    // qualified (ManagedNameQualifier) is still a single operand
    private static ExpressionSyntax Parenthesize(ExpressionSyntax written, ExpressionSyntax rewritten) =>
        written is LiteralExpressionSyntax or IdentifierNameSyntax or ParenthesizedExpressionSyntax
            ? rewritten
            : SyntaxFactory.ParenthesizedExpression(rewritten);

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
