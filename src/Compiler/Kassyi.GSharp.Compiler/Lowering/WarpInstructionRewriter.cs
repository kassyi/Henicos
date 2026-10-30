namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Hoists nested warp-level primitives and calls to device functions that may suspend into standalone statements, so that
/// the state machine can split on them.
/// </summary>
/// <param name="semanticModel">
/// A semantic model of the compilation, which tells whether a later argument of a suspending call or a warp shuffle
/// writes the variable that an earlier one reads (<see cref="ExpressionEffects"/>) and which arguments are constant.
/// Without it, the arguments are compared by name and only literals count as constant.
/// </param>
public class WarpInstructionRewriter(SemanticModel? semanticModel = null) : CSharpSyntaxRewriter
{
    private readonly SemanticModel? _semanticModel = semanticModel;
    private int _tempCounter;

    /// <summary>Rewrites expressions containing warp primitives by hoisting them into prior statements.</summary>
    public BlockSyntax Rewrite(BlockSyntax block) => (BlockSyntax)Visit(block);

    public override SyntaxNode VisitBlock(BlockSyntax node) => node.WithStatements(RewriteStatements(node.Statements));

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        var statements = RewriteStatements(node.Statements);

        // [WHY] A declaration directly under a case label is skipped by jumps to later labels, which C++ rejects
        return statements.Count == node.Statements.Count
            ? node.WithStatements(statements)
            : node.WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(SyntaxFactory.Block(statements)));
    }

    // [WHY] A temporary lands right before its statement in the same list, so it stays in the scope of loop variables
    // and is evaluated on every iteration of the enclosing loop.
    private SyntaxList<StatementSyntax> RewriteStatements(SyntaxList<StatementSyntax> statements)
    {
        var newStatements = ImmutableArray.CreateBuilder<StatementSyntax>(statements.Count);

        foreach (var original in statements)
        {
            var hoistedAssignments = ImmutableArray.CreateBuilder<StatementSyntax>();
            var statement = StoreDiscardedResult(original);

            // Rewrite the statement and capture any hoisted warp instructions
            var rewrittenStatement =
                new StatementRewriter(this, statement, hoistedAssignments).Visit(statement) as StatementSyntax
                ?? throw new InvalidOperationException("Warp instruction hoisting removed a statement.");

            newStatements.AddRange(hoistedAssignments);
            newStatements.Add(rewrittenStatement);
        }

        return SyntaxFactory.List(newStatements);
    }

    // [WHY] The state machine of a device function that returns a value always writes the result through a target, so a
    // call statement that discards the value gets a temporary to write it to.
    private StatementSyntax StoreDiscardedResult(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } &&
        BarrierHelper.IsValueReturningSuspendingCall(call)
            ? StatementRewriter.CreateTempDeclaration(GetNextCallTempName(), call.WithoutTrivia())
                .WithTriviaFrom(statement)
            : statement;

    private string GetNextTempName() => $"{GSharpInternalNames.Lowering.WarpTempPrefix}{_tempCounter++}";

    private string GetNextCallTempName() => $"{GSharpInternalNames.Lowering.CallTempPrefix}{_tempCounter++}";

    private string GetNextArgumentTempName() => $"{GSharpInternalNames.Lowering.ArgumentTempPrefix}{_tempCounter++}";

    private static bool IsWarpPrimitive(InvocationExpressionSyntax inv) =>
        BarrierHelper.IsWarpShuffle(inv) || BarrierHelper.IsSuspendingCall(inv);

    private static bool ContainsWarpPrimitive(SyntaxNode node) =>
        node.DescendantNodes()
            .AsStackEnumerable()
            .Any(static descendant => descendant is InvocationExpressionSyntax inv && IsWarpPrimitive(inv));

    private class StatementRewriter(
        WarpInstructionRewriter parent,
        StatementSyntax rootStatement,
        ImmutableArray<StatementSyntax>.Builder hoistedAssignments)
        : CSharpSyntaxRewriter
    {
        // Nested statement lists hoist into their own scope instead of in front of the root statement
        public override SyntaxNode VisitBlock(BlockSyntax node) => parent.VisitBlock(node);

        public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node) => parent.VisitSwitchSection(node);

        // A loop header is evaluated in every iteration, not once in front of the loop. LoopRewriter flattens a loop whose
        // header holds a warp primitive, and the second hoisting pass then finds the header in the flat statement list.
        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node) =>
            node.WithStatement(VisitLoopBody(node.Statement));

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node) =>
            node.WithStatement(VisitLoopBody(node.Statement));

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node) =>
            node.WithStatement(VisitLoopBody(node.Statement));

        private StatementSyntax VisitLoopBody(StatementSyntax body) =>
            Visit(body) as StatementSyntax ??
            throw new InvalidOperationException("Warp instruction hoisting removed a loop body.");

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            // An embedded statement without braces ('for (...) x += Shuffle(...);') has no list for the temporary
            if (node is StatementSyntax statement and not BlockSyntax &&
                statement != rootStatement &&
                statement.Parent is not (BlockSyntax or SwitchSectionSyntax or LabeledStatementSyntax) &&
                ContainsWarpPrimitive(statement))
            {
                return parent.VisitBlock(SyntaxFactory.Block(statement));
            }

            return base.Visit(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            // First, visit children to handle nested warp instructions (e.g., WarpShuffle(WarpShuffle(x)))
            var visited = base.VisitInvocationExpression(node);
            if (visited is not InvocationExpressionSyntax visitedNode)
            {
                return visited;
            }

            if (!IsWarpPrimitive(visitedNode))
            {
                return visitedNode;
            }

            // [WHY] A shuffle is split as well: its value is published before the state boundary and read again after
            // it, together with the other arguments in parameter order (#788)
            var isSuspendingCall = BarrierHelper.IsSuspendingCall(visitedNode);
            visitedNode = SpillArguments(visitedNode);

            switch (node.Parent)
            {
                // If it's already a statement, no need to hoist (e.g. SyncWarp or a standalone WarpShuffle)
                case ExpressionStatementSyntax:
                // If it's already perfectly hoisted, skip
                case EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax vd } when
                    vd.Identifier.Text.StartsWith(GSharpInternalNames.Lowering.WarpTempPrefix,
                        StringComparison.Ordinal) ||
                    vd.Identifier.Text.StartsWith(GSharpInternalNames.Lowering.CallTempPrefix,
                        StringComparison.Ordinal):

                    return visitedNode;
            }

            var tempName = isSuspendingCall ? parent.GetNextCallTempName() : parent.GetNextTempName();

            // Create: var <temp> = <invocation>;
            hoistedAssignments.Add(CreateTempDeclaration(tempName, visitedNode));

            return SyntaxFactory.IdentifierName(tempName).WithTriviaFrom(visitedNode);
        }

        // [WHY] A suspended call is executed again on every resume, and a warp shuffle reads its arguments on both sides of
        // its state boundary, so each argument must evaluate to the same value: an argument with side effects or one that
        // reads memory other threads change is evaluated once, before the call, in the order written (named arguments
        // too, whatever their parameter order).
        private InvocationExpressionSyntax SpillArguments(InvocationExpressionSyntax call)
        {
            var arguments = call.ArgumentList.Arguments;
            var spilledArguments = ImmutableArray.CreateBuilder<ArgumentSyntax>(arguments.Count);
            var changed = false;
            for (var i = 0; i < arguments.Count; i++)
            {
                var argument = arguments[i];
                var spilled = SpillArgument(arguments, i);
                changed |= spilled != argument;
                spilledArguments.Add(spilled);
            }

            return changed
                ? call.WithArgumentList(call.ArgumentList.WithArguments(
                    SyntaxFactory.SeparatedList(spilledArguments, arguments.GetSeparators())))
                : call;
        }

        // [WHY] An argument, or an index that selects a ref argument, can stay where it stands only when it has no side
        // effect and the same value before and after the arguments that follow it: those are spilled and run before the
        // call, while what stays is read at the call. This is the one rule for both (#788 review).
        private bool CanStayInPlace(ExpressionSyntax expression, SeparatedSyntaxList<ArgumentSyntax> arguments,
            int index) =>
            expression is LiteralExpressionSyntax ||
            (expression is IdentifierNameSyntax identifier && !IsWrittenByLaterArgument(identifier, arguments, index)) ||
            IsConstant(expression);

        // [WHY] C# reads an argument before it evaluates the next one, but an identifier that is not spilled is read at
        // the call, after the spilled later arguments: one whose variable a later argument writes ('S(a, Next(ref a))',
        // 'S(ref a[i], i++)') is spilled as well, so that it keeps the value C# reads (#608).
        private bool IsWrittenByLaterArgument(IdentifierNameSyntax identifier,
            SeparatedSyntaxList<ArgumentSyntax> arguments, int index)
        {
            var model = parent._semanticModel;
            if (model == null)
            {
                return arguments
                    .AsStackEnumerable()
                    .Skip(index + 1)
                    .Any(identifier.Identifier.ValueText, static (later, name) => WritesByName(later, name));
            }

            var read = ExpressionEffects.Of(identifier, model);
            return arguments
                .AsStackEnumerable()
                .Skip(index + 1)
                .Any((Read: read, Model: model), static (later, s) => s.Read.InterferesWith(
                    later.RefKindKeyword.IsKind(SyntaxKind.None)
                        ? ExpressionEffects.Of(later.Expression, s.Model)
                        : ExpressionEffects.OfLocation(later.Expression, s.Model)
                ));
        }

        // Without a semantic model: whether argument holds a ref / out argument, an assignment or an increment of the
        // variable named name
        private static bool WritesByName(ArgumentSyntax argument, string name) =>
            argument
                .DescendantNodes()
                .AsStackEnumerable()
                .Any(name, static (node, n) => node switch
                {
                    ArgumentSyntax { RefKindKeyword.RawKind: not (int)SyntaxKind.None } byRef =>
                        IsNamed(byRef.Expression, n),
                    AssignmentExpressionSyntax assignment => IsNamed(assignment.Left, n),
                    PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.PreIncrementExpression,
                        SyntaxKind.PreDecrementExpression) => IsNamed(prefix.Operand, n),
                    PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.PostIncrementExpression,
                        SyntaxKind.PostDecrementExpression) => IsNamed(postfix.Operand, n),
                    _ => false
                });

        private static bool IsNamed(ExpressionSyntax expression, string name) =>
            expression is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == name;

        private ArgumentSyntax SpillArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, int index)
        {
            var argument = arguments[index];
            if (argument.Expression is DeclarationExpressionSyntax
                {
                    Designation: SingleVariableDesignationSyntax designation
                } declarationExpression)
            {
                // 'out int x' declares a local that the callee writes on its last resume, so it is declared before the
                // call. The declarator and the reference keep the span of the designation, which names the local.
                var annotations = ImmutableArray.CreateRange(
                    designation.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)
                );
                var identifier = designation.Identifier.WithoutTrivia();
                // <type> x;
                var declarator = SyntaxFactory.VariableDeclarator(identifier).WithAdditionalAnnotations(annotations);
                hoistedAssignments.Add(
                    SyntaxFactory.LocalDeclarationStatement(
                        SyntaxFactory.VariableDeclaration(
                            declarationExpression.Type.WithoutTrivia(),
                            SyntaxFactory.SingletonSeparatedList(declarator)
                        )
                    ).NormalizeWhitespace()
                );

                return argument.WithExpression(SyntaxFactory.IdentifierName(identifier)
                    .WithAdditionalAnnotations(annotations)
                    .WithTriviaFrom(declarationExpression));
            }

            if (!argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                // The reference itself must stay a variable, so only the indices that select it are evaluated once
                return argument.Expression is ElementAccessExpressionSyntax elementAccess
                    ? argument.WithExpression(SpillIndices(elementAccess, arguments, index))
                    : argument;
            }

            return CanStayInPlace(argument.Expression, arguments, index)
                ? argument
                : argument.WithExpression(Spill(argument.Expression));
        }

        // [WHY] A constant expression has no side effect and the same value on every evaluation, like a literal. Left in
        // place, it also keeps its implicit constant conversion ('Half / 2' to a uint parameter), which a variable lacks.
        private bool IsConstant(ExpressionSyntax expression) =>
            parent._semanticModel is { } model && model.GetSafeConstantValue(expression).HasValue;

        // index is the position of the ref argument that the element access is, among the arguments of the call
        private ElementAccessExpressionSyntax SpillIndices(ElementAccessExpressionSyntax elementAccess,
            SeparatedSyntaxList<ArgumentSyntax> arguments, int index)
        {
            // Outer indices of a jagged access are evaluated before inner ones, so the receiver is spilled first
            var receiver = elementAccess.Expression is ElementAccessExpressionSyntax inner
                ? SpillIndices(inner, arguments, index)
                : elementAccess.Expression;

            var indices = elementAccess.ArgumentList.Arguments;
            var spilledIndices = ImmutableArray.CreateBuilder<ArgumentSyntax>(indices.Count);
            foreach (var elementIndex in indices)
            {
                spilledIndices.Add(CanStayInPlace(elementIndex.Expression, arguments, index)
                    ? elementIndex
                    : elementIndex.WithExpression(Spill(elementIndex.Expression)));
            }

            return elementAccess
                .WithExpression(receiver)
                .WithArgumentList(elementAccess.ArgumentList.WithArguments(
                    SyntaxFactory.SeparatedList(spilledIndices, indices.GetSeparators())));
        }

        private IdentifierNameSyntax Spill(ExpressionSyntax expression)
        {
            var tempName = parent.GetNextArgumentTempName();
            hoistedAssignments.Add(CreateTempDeclaration(tempName, expression.WithoutTrivia(),
                new SyntaxAnnotation(LoweringAnnotations.ArgumentTempAnnotationKind)));
            return SyntaxFactory.IdentifierName(tempName).WithTriviaFrom(expression);
        }

        // var <tempName> = <value>;
        internal static LocalDeclarationStatementSyntax CreateTempDeclaration(string tempName, ExpressionSyntax value,
            params SyntaxAnnotation[] declaratorAnnotations)
        {
            var declarator = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(tempName))
                .WithInitializer(SyntaxFactory.EqualsValueClause(value))
                .WithAdditionalAnnotations(declaratorAnnotations);
            return SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.IdentifierName("var"),
                    SyntaxFactory.SingletonSeparatedList(declarator)
                )
            ).NormalizeWhitespace();
        }
    }
}

