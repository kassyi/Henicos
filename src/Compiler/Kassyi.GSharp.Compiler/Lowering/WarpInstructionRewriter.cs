using Microsoft.CodeAnalysis.Operations;

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
        // [WHY] The temporaries stay directly in the section, which the C++ braces (TranspileSwitchSection). A block
        // around them would end the C# scope of the section's locals, which another section may use (#795).
        return node.WithStatements(RewriteStatements(node.Statements));
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
            InvocationExpressionSyntax visitedNode;
            if (IsWarpPrimitive(node) && HasNestedWarpPrimitiveArgument(node))
            {
                visitedNode = VisitArgumentsInOrder(node);
            }
            else
            {
                // First, visit children to handle nested warp instructions (e.g., WarpShuffle(WarpShuffle(x)))
                var visited = base.VisitInvocationExpression(node);
                if (visited is not InvocationExpressionSyntax visitedInvocation)
                {
                    return visited;
                }

                if (!IsWarpPrimitive(visitedInvocation))
                {
                    return visitedInvocation;
                }

                // [WHY] A shuffle is split as well: its value is published before the state boundary and read again
                // after it, together with the other arguments in parameter order (#788)
                visitedNode = SpillArguments(visitedInvocation);
            }

            var isSuspendingCall = BarrierHelper.IsSuspendingCall(visitedNode);

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

        private static bool HasNestedWarpPrimitiveArgument(InvocationExpressionSyntax call) =>
            call.ArgumentList.Arguments
                .AsStackEnumerable()
                .Any(static argument => ContainsWarpPrimitive(argument));

        // [WHY] C# evaluates the arguments in the order written, so an argument that holds a suspending call or a shuffle
        // runs after the arguments before it and before the ones after it. Hoisting the nested call first (the children
        // before the call) would run it before the earlier arguments are evaluated, which then see what it wrote. Each
        // argument is hoisted and evaluated once in turn instead, so the nested call lands between them (#788 review).
        // <invocation>(<spilled argument 0>, <nested call temp>, ...)
        private InvocationExpressionSyntax VisitArgumentsInOrder(InvocationExpressionSyntax call)
        {
            var expression = Visit(call.Expression) as ExpressionSyntax
                             ?? throw new InvalidOperationException("Warp instruction hoisting removed a callee.");
            var arguments = call.ArgumentList.Arguments;
            for (var i = 0; i < arguments.Count; i++)
            {
                var visited = Visit(arguments[i]) as ArgumentSyntax
                              ?? throw new InvalidOperationException("Warp instruction hoisting removed an argument.");
                arguments = arguments.Replace(arguments[i], visited);
                arguments = arguments.Replace(arguments[i], SpillArgument(arguments, i));
            }

            return call
                .WithExpression(expression)
                .WithArgumentList(call.ArgumentList.WithArguments(arguments));
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

        // [WHY] What C# evaluates once, when it evaluates the argument, must not be evaluated again at the call: the
        // spilled arguments run before it, and a suspended call evaluates what stays in place again on every resume,
        // after the callee may have written through its ref / out arguments. An argument, an index or the array of a
        // by-reference argument stays in place only when it is a literal, a constant, or a local or by-value parameter
        // that no other name reaches (no ref local, ref parameter or taken address) and that no other argument of the
        // call writes, before or after it, also by passing it by ref / out (#788). This is the one rule for all three.
        private bool CanStayInPlace(ExpressionSyntax expression, SeparatedSyntaxList<ArgumentSyntax> arguments,
            int index) =>
            expression is LiteralExpressionSyntax ||
            IsConstant(expression) ||
            (expression is IdentifierNameSyntax identifier &&
             IsUnaliasedVariable(identifier) &&
             !IsWrittenByOtherArgument(identifier, arguments, index));

        // A local or by-value parameter that has no other name. A name without a symbol is a temporary of lowering.
        private bool IsUnaliasedVariable(IdentifierNameSyntax identifier)
        {
            if (parent._semanticModel is not { } model)
            {
                return true;
            }

            return model.GetSymbol(identifier) switch
            {
                null => true,
                ILocalSymbol { IsRef: false } local => !AliasedVariables.Contains(local, model.Compilation),
                IParameterSymbol { RefKind: RefKind.None } parameter =>
                    !AliasedVariables.Contains(parameter, model.Compilation),
                _ => false
            };
        }

        // [WHY] C# reads an argument before it evaluates the next one, but an identifier that is not spilled is read at
        // the call, after the spilled arguments, and again on every resume: one whose variable another argument writes
        // ('S(a, Next(ref a))', 'S(ref a[i], i++)', 'S(ref x, x)' with a callee that writes x) is spilled as well, so
        // that it keeps the value C# reads (#608). An argument passed by ref / out writes its variable.
        private bool IsWrittenByOtherArgument(IdentifierNameSyntax identifier,
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
            for (var i = 0; i < arguments.Count; i++)
            {
                if (i != index && read.InterferesWith(GetArgumentEffects(arguments[i], model)))
                {
                    return true;
                }
            }

            return false;
        }

        // What evaluating an argument and passing it reads and writes: a ref / out argument also writes its variable,
        // which the callee may change before it suspends
        private static ExpressionEffects GetArgumentEffects(ArgumentSyntax argument, SemanticModel model)
        {
            if (argument.RefKindKeyword.IsKind(SyntaxKind.None) || argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
            {
                return argument.RefKindKeyword.IsKind(SyntaxKind.None)
                    ? ExpressionEffects.Of(argument.Expression, model)
                    : ExpressionEffects.OfLocation(argument.Expression, model);
            }

            return ExpressionEffects.OfLocation(argument.Expression, model)
                .Union(new ExpressionEffects(default, ExpressionEffects.RootOf(argument.Expression, model)));
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

            if (IsPassedByReference(argument))
            {
                // The reference itself must stay a variable, so only what selects it is evaluated once
                return argument.WithExpression(SpillLocation(argument.Expression, arguments, index));
            }

            return CanStayInPlace(argument.Expression, arguments, index)
                ? argument
                : argument.WithExpression(Spill(argument.Expression));
        }

        // [WHY] A constant expression has no side effect and the same value on every evaluation, like a literal. Left in
        // place, it also keeps its implicit constant conversion ('Half / 2' to a uint parameter), which a variable lacks.
        private bool IsConstant(ExpressionSyntax expression) =>
            parent._semanticModel is { } model && model.GetSafeConstantValue(expression).HasValue;

        // [WHY] A ref, out or in argument names a storage location, and C# fixes which one when it evaluates the
        // argument: the array of an element and its indices are evaluated once (in the order written: the array, then
        // each index, outer accesses first), and a member access selects within its receiver. The location itself is not
        // copied, so that the callee writes and reads the variable (#788). index is the position of the argument.
        private ExpressionSyntax SpillLocation(ExpressionSyntax location, SeparatedSyntaxList<ArgumentSyntax> arguments,
            int index)
        {
            switch (location)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    return parenthesized.WithExpression(SpillLocation(parenthesized.Expression, arguments, index));
                case MemberAccessExpressionSyntax { RawKind: (int)SyntaxKind.SimpleMemberAccessExpression } member:
                    return member.WithExpression(SpillLocation(member.Expression, arguments, index));
                case ElementAccessExpressionSyntax elementAccess:
                    var receiver = SpillReceiver(elementAccess.Expression, arguments, index);
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
                default:
                    return location;
            }
        }

        // The receiver of an element access in a location: an array (a reference) is evaluated once like an index; a
        // stack array, a span or a struct that holds the elements is itself a location. Without a semantic model only
        // the indices of a nested access are evaluated once.
        private ExpressionSyntax SpillReceiver(ExpressionSyntax receiver, SeparatedSyntaxList<ArgumentSyntax> arguments,
            int index)
        {
            if (parent._semanticModel is not { } model ||
                model.GetTypeSymbol(receiver) is not IArrayTypeSymbol ||
                StackArrayLocals.IsStackArrayReference(receiver, model))
            {
                return SpillLocation(receiver, arguments, index);
            }

            return CanStayInPlace(receiver, arguments, index) ? receiver : Spill(receiver);
        }

        // A ref, out or in argument, or an argument of an in / ref readonly parameter that names a variable, which C#
        // passes by reference too (an argument that needs a conversion or is not a variable is passed as a copy)
        // (the operation of the expression is the argument's value only when no conversion sits between them)
        private bool IsPassedByReference(ArgumentSyntax argument) =>
            !argument.RefKindKeyword.IsKind(SyntaxKind.None) ||
            (parent._semanticModel?.GetSafeOperation(argument.Expression) is
                (ILocalReferenceOperation or IParameterReferenceOperation or IArrayElementReferenceOperation
                or IFieldReferenceOperation) and
                {
                    Parent: IArgumentOperation { Parameter.RefKind: RefKind.In or RefKind.RefReadOnlyParameter }
                });

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

