namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Turns a short-circuit operator (<c>&amp;&amp;</c>, <c>||</c>) or a conditional operator (<c>?:</c>) whose
/// conditionally evaluated operand holds a warp primitive or a call that may suspend into an <c>if</c> statement.
/// </summary>
/// <remarks>
/// <see cref="WarpInstructionRewriter"/> hoists such calls in front of their statement, where they would run
/// unconditionally. After this pass the call sits in a branch that <see cref="BranchRewriter"/> lowers like any other.
/// <code>
/// bool ok = a &amp;&amp; S();   →   var t = a; if (t) { t = S(); } bool ok = t;
/// T c = a &amp;&amp; S();     →   var l = default; l = a; var r = default; if (__gsharp_truth_test(l)) { r = l; } else { r = l &amp; S(); } T c = r;
/// int v = c ? S() : x;   →   var t = default; if (c) { t = S(); } else { t = x; } int v = t;
/// </code>
/// The temporary of a conditional operator or a user-defined short-circuit operator is initialized with <c>default</c>
/// carrying the span of the original operator, which is how <see cref="Transpiler.HoistedFieldTypeResolver"/> finds its
/// type.
/// </remarks>
/// <param name="names">
/// The names of the temporaries of the method, shared with the other passes that declare temporaries; a new set when
/// omitted.
/// </param>
/// <param name="semanticModel">
/// A semantic model of the body, used to recognize user-defined <c>&amp;&amp;</c> / <c>||</c> operators and to keep the
/// C# evaluation order of an assignment target before a user-defined conditional.
/// </param>
public class ConditionalEvaluationRewriter(LoweringTempNames? names = null, SemanticModel? semanticModel = null)
    : CSharpSyntaxRewriter, IBlockLoweringPass
{
    private readonly LoweringTempNames _names = names ?? new LoweringTempNames();
    private readonly SemanticModel? _semanticModel = semanticModel;

    /// <summary>Rewrites the conditional operators in the block that need it.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) =>
        Visit(block) as BlockSyntax ??
        throw new InvalidOperationException("Conditional evaluation lowering removed the block.");

    public override SyntaxNode VisitBlock(BlockSyntax node) => node.WithStatements(RewriteStatements(node.Statements));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        // [WHY] The temporaries stay directly in the section, which the C++ braces (TranspileSwitchSection). A block
        // around them would end the C# scope of the section's locals, which another section may use (#795).
        return node.WithStatements(RewriteStatements(node.Statements));
    }

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        // An embedded statement without braces ('if (c) x = a && S();') has no list for the temporary
        if (node is StatementSyntax statement and not BlockSyntax &&
            statement.Parent is not (BlockSyntax or SwitchSectionSyntax or LabeledStatementSyntax) &&
            FindConditional(statement) != null)
        {
            return VisitBlock(SyntaxFactory.Block(statement));
        }

        return base.Visit(node);
    }

    // Lambdas and local functions are not part of the state machine (GSH0062)
    public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) => node;

    public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) => node;

    public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) => node;

    private SyntaxList<StatementSyntax> RewriteStatements(SyntaxList<StatementSyntax> statements)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count);
        var pending = new Stack<StatementSyntax>();

        for (var i = statements.Count - 1; i >= 0; i--)
        {
            pending.Push(statements[i]);
        }

        // A rewritten statement goes back on the stack: its operands may hold further conditional operators
        while (pending.Count > 0)
        {
            var statement = pending.Pop();
            // [WHY] A statement the split pass of the IR wrote out has no conditional left that holds a split
            if (statement.HasAnnotations(LoweringAnnotations.IrLoweredAnnotationKind))
            {
                newStatements.Add(statement);
                continue;
            }

            if (FindConditional(statement) is not { } conditional)
            {
                newStatements.Add(Visit(statement) as StatementSyntax
                                  ?? throw new InvalidOperationException(
                                      "Conditional evaluation lowering removed a statement."));
                continue;
            }

            if (conditional is BinaryExpressionSyntax logical && IsUserDefined(logical))
            {
                var (targetPrefix, spilledLeft) = SpillAssignmentTarget(statement, logical);
                var (prefix, temp) = LowerUserDefined(logical);
                var replacement = temp.WithTriviaFrom(logical);
                var rewrittenStatement = spilledLeft != null &&
                                         statement is ExpressionStatementSyntax
                                         {
                                             Expression: AssignmentExpressionSyntax assignment
                                         } expressionStatement
                    ? expressionStatement.WithExpression(
                        assignment
                            .WithLeft(spilledLeft.WithTriviaFrom(assignment.Left))
                            .WithRight(assignment.Right.ReplaceNode(logical, replacement)))
                    : statement.ReplaceNode(logical, replacement);
                pending.Push(rewrittenStatement);
                for (var i = prefix.Length - 1; i >= 0; i--)
                {
                    pending.Push(prefix[i]);
                }

                for (var i = targetPrefix.Length - 1; i >= 0; i--)
                {
                    pending.Push(targetPrefix[i]);
                }

                continue;
            }

            var (builtinPrefix, builtinTemp) = Lower(conditional);
            pending.Push(statement.ReplaceNode(conditional, builtinTemp.WithTriviaFrom(conditional)));
            for (var i = builtinPrefix.Length - 1; i >= 0; i--)
            {
                pending.Push(builtinPrefix[i]);
            }
        }

        return SyntaxFactory.List(newStatements);
    }

    // A && or || that calls a user-defined & or | and tests its left operand with operator false or true
    private bool IsUserDefined(BinaryExpressionSyntax binary) =>
        _semanticModel != null &&
        OperatorMacroSyntax.GetOperator(binary, _semanticModel) is
        {
            Method: not null,
            IsLifted: false,
            Test: not null
        };

    // Spills the parts of a simple assignment's target (indices, array or pointer receiver, pointer of *p / p->F)
    // before a user-defined && or || in its right operand, so C# evaluates the target parts before the left operand
    // while keeping any element check on the final store
    private (ImmutableArray<StatementSyntax> Prefix, ExpressionSyntax? SpilledLeft) SpillAssignmentTarget(
        StatementSyntax statement,
        BinaryExpressionSyntax conditional)
    {
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                } assignment
            } ||
            !IsDescendant(conditional, assignment.Right))
        {
            return (ImmutableArray<StatementSyntax>.Empty, null);
        }

        var arguments = SyntaxFactory.SeparatedList(
            [
                SyntaxFactory.Argument(assignment.Left.WithoutTrivia())
                    .WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)),
                SyntaxFactory.Argument(assignment.Right.WithoutTrivia())
            ]
        );
        var prefix = ImmutableArray.CreateBuilder<StatementSyntax>();
        var compilation = _semanticModel?.Compilation;
        var spilledLeft = LocationPartSpiller.SpillLocation(
            assignment.Left,
            arguments,
            0,
            _semanticModel,
            expression =>
            {
                var tempName = _names.NextArgument();
                prefix.Add(LoweringSyntax.DeclareTemporary(
                    tempName,
                    expression.WithoutTrivia().WithSpilledTypeSpanFrom(expression, compilation),
                    expression,
                    compilation,
                    new SyntaxAnnotation(LoweringAnnotations.ArgumentTempAnnotationKind)));
                return SyntaxFactory.IdentifierName(tempName)
                    .WithSpilledTypeSpanFrom(expression, compilation)
                    .WithTriviaFrom(expression);
            });
        return prefix.Count == 0
            ? (ImmutableArray<StatementSyntax>.Empty, null)
            : (prefix.ToImmutable(), spilledLeft);
    }

    private static bool IsDescendant(SyntaxNode node, SyntaxNode ancestor)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    // var <left> = default; <left> = <x>; var <result> = default;
    // if (__gsharp_truth_test(<left>)) { <result> = <left>; } else { <result> = <left> & <y>; }
    private (ImmutableArray<StatementSyntax> Prefix, IdentifierNameSyntax Temp) LowerUserDefined(
        BinaryExpressionSyntax binary)
    {
        var compilation = _semanticModel?.Compilation;
        var leftName = _names.NextCondition();
        var resultName = _names.NextCondition();
        var left = SyntaxFactory.IdentifierName(leftName).WithSpilledTypeSpanFrom(binary, compilation);
        var result = SyntaxFactory.IdentifierName(resultName).WithSpilledTypeSpanFrom(binary, compilation);
        var testArguments = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(left))
        );
        var test = SyntaxFactory.InvocationExpression(
            SyntaxFactory.IdentifierName(GSharpInternalNames.Lowering.TruthTest),
            testArguments
        ).WithOriginalSpanFrom(binary);
        var operatorKind = binary.IsKind(SyntaxKind.LogicalAndExpression)
            ? SyntaxKind.BitwiseAndExpression
            : SyntaxKind.BitwiseOrExpression;
        var call = SyntaxFactory.BinaryExpression(operatorKind, left, binary.Right.WithoutTrivia())
            .WithOriginalSpanFrom(binary);
        var statements = ImmutableArray.Create<StatementSyntax>(
            LoweringSyntax.DeclareLocal(leftName, TypedDefault(binary)),
            Assign(left, binary.Left),
            LoweringSyntax.DeclareLocal(resultName, TypedDefault(binary)),
            SyntaxFactory.IfStatement(
                test,
                SyntaxFactory.Block(Assign(result, left)),
                SyntaxFactory.ElseClause(SyntaxFactory.Block(Assign(result, call)))
            )
        );
        return (statements, result);
    }

    private static LiteralExpressionSyntax TypedDefault(ExpressionSyntax typed) =>
        SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression).WithOriginalSpanFrom(typed);

    private (ImmutableArray<StatementSyntax> Prefix, IdentifierNameSyntax Temp) Lower(ExpressionSyntax conditional)
    {
        var name = _names.NextCondition();
        var temp = SyntaxFactory.IdentifierName(name);

        switch (conditional)
        {
            case BinaryExpressionSyntax binary:
            {
                // var t = left; if (t) / if (!t) { t = right; }
                ExpressionSyntax guard = binary.IsKind(SyntaxKind.LogicalAndExpression)
                    ? temp
                    : SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, temp);
                var statements = ImmutableArray.Create<StatementSyntax>(
                    LoweringSyntax.DeclareLocal(name, binary.Left.WithoutTrivia()),
                    SyntaxFactory.IfStatement(guard, SyntaxFactory.Block(Assign(temp, binary.Right)))
                );
                return (statements, temp);
            }
            case ConditionalExpressionSyntax ternary:
            {
                // var t = default; if (c) { t = whenTrue; } else { t = whenFalse; }
                var typedDefault = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression)
                    .WithOriginalSpanFrom(ternary);
                var statements = ImmutableArray.Create<StatementSyntax>(
                    LoweringSyntax.DeclareLocal(name, typedDefault),
                    SyntaxFactory.IfStatement(
                        ternary.Condition.WithoutTrivia(),
                        SyntaxFactory.Block(Assign(temp, ternary.WhenTrue)),
                        SyntaxFactory.ElseClause(SyntaxFactory.Block(Assign(temp, ternary.WhenFalse)))
                    )
                );
                return (statements, temp);
            }
            default:
                throw new InvalidOperationException(
                    $"Cannot lower a conditional expression of kind '{conditional.Kind()}'.");
        }
    }

    /// <summary>
    /// The first conditional operator, in evaluation order, of the statement's own expressions whose conditionally
    /// evaluated operand holds a warp primitive or a call that may suspend.
    /// </summary>
    /// <remarks>
    /// Nested statements, lambdas and loop headers are left out: nested statements have their own list, and a loop
    /// header is lowered after <see cref="LoopRewriter"/> has turned it into a statement of the flat list.
    /// </remarks>
    private static ExpressionSyntax? FindConditional(StatementSyntax statement)
    {
        if (statement is WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax
            or LabeledStatementSyntax)
        {
            return null;
        }

        foreach (var node in statement.DescendantNodes(child =>
                     child == statement ||
                     child is not (StatementSyntax or LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)))
        {
            switch (node)
            {
                case BinaryExpressionSyntax binary when
                    binary.IsKind(SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression) &&
                    ContainsWarpPrimitive(binary.Right):
                    return binary;
                case ConditionalExpressionSyntax ternary when
                    ContainsWarpPrimitive(ternary.WhenTrue) || ContainsWarpPrimitive(ternary.WhenFalse):
                    return ternary;
            }
        }

        return null;
    }

    private static bool ContainsWarpPrimitive(SyntaxNode node) =>
        node.DescendantNodesAndSelf()
            .AsStackEnumerable()
            .Any(static descendant =>
                descendant is InvocationExpressionSyntax invocation &&
                (BarrierHelper.IsWarpShuffle(invocation) || BarrierHelper.IsSuspendingCall(invocation))
            );

    // <target> = <value>;
    private static ExpressionStatementSyntax Assign(IdentifierNameSyntax target, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, target, value.WithoutTrivia())
        );
}
