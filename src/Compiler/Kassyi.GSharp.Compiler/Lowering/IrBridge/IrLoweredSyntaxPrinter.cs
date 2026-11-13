using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Writes a statement whose split plan was applied on the IR (<see cref="IrLoweredStatement"/>) back as the syntax the
/// lowering passes after it read: a copy of the prepared statement in which the nodes replaced by temporaries are
/// replaced, and the few statements the syntax-based passes build for the same purpose.
/// </summary>
/// <remarks>
/// The passes after it find the type and symbol of every node through its original span, so no node is built from the
/// IR: an expression is always the copy of its syntax (<see cref="IrSyntaxIndex"/>). What is built:
/// <list type="bullet">
/// <item><description><c>var t = &lt;copy&gt;;</c>, as <see cref="WarpInstructionRewriter"/> declares a temporary (the
/// temporary of an operand carries <see cref="LoweringAnnotations.ArgumentTempAnnotationKind"/>);</description></item>
/// <item><description><c>var t = default;</c> / <c>var t = &lt;left&gt;;</c>, <c>if (...) { ...; t = ...; }</c>, as
/// <see cref="ConditionalEvaluationRewriter"/> writes a conditional;</description></item>
/// <item><description><c>&lt;type&gt; x;</c> before a call that splits and declares <c>out &lt;type&gt; x</c>;
/// </description></item>
/// <item><description><c>t op= &lt;value&gt;; &lt;target&gt; = t;</c>, the completion of a compound assignment (the
/// first is a copy of the compound assignment with its target replaced, so it keeps its original
/// span);</description></item>
/// <item><description><c>{ var t = &lt;location&gt;; }</c>, the check of a location: the block ends the scope of the
/// temporary before any split, so it never becomes a frame field.</description></item>
/// </list>
/// A reference to a temporary carries no original span: the identity of a temporary is its name
/// (<see cref="LoweringIdentity"/>), and a name without a symbol is what <see cref="SpilledArguments"/> leaves in
/// place.
/// A node with no syntax to copy makes the statement unprintable, never a stand-in.
/// </remarks>
/// <param name="index">The syntax of the prepared statement.</param>
/// <param name="names">The names of the temporaries of the method.</param>
/// <param name="compilation">The compilation of the method.</param>
internal sealed class IrLoweredSyntaxPrinter(IrSyntaxIndex index, LoweringTempNames names, Compilation compilation)
{
    private readonly Dictionary<IrVariable, Temporary> _temporaries = [];
    private readonly Dictionary<IrVariable, IrSpillTemporary> _made = [];

    /// <summary>
    /// Adds the statements of <paramref name="lowered"/> to <paramref name="output"/>; <see langword="false"/> when a
    /// node has no syntax to copy, and then <paramref name="output"/> is incomplete and the names given are lost.
    /// </summary>
    /// <param name="lowered">The statement with its plan applied.</param>
    /// <param name="prepared">The statement of the prepared body it was planned for.</param>
    /// <param name="output">The statements written.</param>
    /// <param name="remaining">The copy of what is left of the statement, when something is left.</param>
    public bool TryPrint(IrLoweredStatement lowered, StatementSyntax prepared,
        ImmutableArray<StatementSyntax>.Builder output, out StatementSyntax? remaining)
    {
        remaining = null;
        foreach (var temporary in lowered.Temporaries)
        {
            _made[temporary.Variable] = temporary;
        }

        foreach (var statement in lowered.Statements)
        {
            if (ReferenceEquals(statement, lowered.Remaining))
            {
                if (!TryPrintRemaining(statement, prepared, output, out remaining))
                {
                    return false;
                }
            }
            else if (!TryPrintStep(statement, output))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The copy of <paramref name="statement"/>, a statement without a plan of a declaration of several locals:
    /// <c>&lt;type&gt; &lt;declarator&gt;;</c>.
    /// </summary>
    public bool TryPrintDeclarator(IrLocalDeclaration statement, LocalDeclarationStatementSyntax prepared,
        [NotNullWhen(true)] out StatementSyntax? printed) =>
        TryPrintDeclaration(statement, prepared, out printed);

    // A statement written before what is left: a temporary, a conditional, a check or the store of a compound
    private bool TryPrintStep(IrStatement statement, ImmutableArray<StatementSyntax>.Builder output) => statement switch
    {
        IrLocalDeclaration { Variable.Kind: IrVariableKind.Temporary } declaration =>
            TryDeclare(declaration, output),
        IrIf @if => TryPrintConditional(@if, output),
        IrExpressionStatement { Expression: IrAssignment { Target: IrDiscard } check } =>
            TryPrintCheck(check, output),
        IrExpressionStatement { Expression: IrAssignment { Target: IrVariableReference target } assignment }
            when target.Variable.Kind == IrVariableKind.Temporary => TryAssignTemporary(assignment, output),
        IrExpressionStatement { Expression: IrCompoundAssignment compound } => TryApplyCompound(compound, output),
        IrExpressionStatement { Expression: IrAssignment { Value: IrVariableReference value } store }
            when value.Variable.Kind == IrVariableKind.Temporary => TryStoreCompound(store, value, output),
        _ => false
    };

    private bool TryPrintRemaining(IrStatement statement, StatementSyntax prepared,
        ImmutableArray<StatementSyntax>.Builder output, out StatementSyntax? remaining)
    {
        remaining = null;
        if (prepared is LocalDeclarationStatementSyntax { Declaration.Variables.Count: > 1 } declaration)
        {
            if (statement is not IrLocalDeclaration local || !TryPrintDeclaration(local, declaration, out remaining))
            {
                return false;
            }

            output.Add(remaining);
            return true;
        }

        // An if keeps its branches: only its condition was planned
        IrNode replaced = statement is IrIf @if ? @if.Condition : statement;
        if (!TryReplaceTemporaries(prepared, replaced, out var copy) || copy is not StatementSyntax printed)
        {
            return false;
        }

        if (statement is IrExpressionStatement { Expression: IrCall { Classification.Split.IsSplit: true } } &&
            printed is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } callStatement)
        {
            printed = callStatement.WithExpression(DeclareOutArguments(call, output));
        }

        remaining = printed;
        output.Add(printed);
        return true;
    }

    // <type> <declarator'>; one local of a declaration of several
    private bool TryPrintDeclaration(IrLocalDeclaration statement, LocalDeclarationStatementSyntax prepared,
        [NotNullWhen(true)] out StatementSyntax? printed)
    {
        printed = null;
        if (!index.TryGet(statement.Source, out var syntax) || syntax is not VariableDeclaratorSyntax declarator ||
            declarator.Parent != prepared.Declaration ||
            !TryReplaceTemporaries(declarator, statement, out var copy) ||
            copy is not VariableDeclaratorSyntax replaced)
        {
            return false;
        }

        printed = prepared.WithDeclaration(
            prepared.Declaration.WithVariables(SyntaxFactory.SingletonSeparatedList(replaced.WithoutTrivia()))
        );
        return true;
    }

    // var t = <value'>; and, for the temporary of a conditional, var t = default; or var t = <left'>;
    private bool TryDeclare(IrLocalDeclaration declaration, ImmutableArray<StatementSyntax>.Builder output)
    {
        if (!_made.TryGetValue(declaration.Variable, out var made) ||
            !index.TryGet(made.Origin.Source, out var origin) || declaration.Initializer is not { } initializer)
        {
            return false;
        }

        if (made.Kind == IrTemporaryKind.Condition)
        {
            return TryDeclareCondition(declaration.Variable, initializer, origin, output);
        }

        if (!TryPrintExpression(initializer, out var value))
        {
            return false;
        }

        if (made.Kind != IrTemporaryKind.Argument && value is InvocationExpressionSyntax call)
        {
            value = DeclareOutArguments(call, output);
        }

        value = WithParentheses(origin, value);

        var name = Name(declaration.Variable, made.Kind, origin);
        output.Add(made.Kind == IrTemporaryKind.Argument
            ? LoweringSyntax.DeclareTemporary(name, value.WithoutTrivia().WithSpilledTypeSpanFrom(origin, compilation),
                origin, compilation, new SyntaxAnnotation(LoweringAnnotations.ArgumentTempAnnotationKind))
            : LoweringSyntax.DeclareTemporary(name, value.WithSpilledTypeSpanFrom(origin, compilation), origin,
                compilation));
        return true;
    }

    // var t = default; (?:, the default carries the span of the conditional, which gives it its type) or
    // var t = <left'>; (&&, ||)
    private bool TryDeclareCondition(IrVariable variable, IrExpression initializer, SyntaxNode origin,
        ImmutableArray<StatementSyntax>.Builder output)
    {
        ExpressionSyntax value;
        if (initializer is IrDefaultValue)
        {
            if (origin is not ConditionalExpressionSyntax)
            {
                return false;
            }

            value = SyntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression).WithOriginalSpanFrom(origin);
        }
        else if (TryPrintExpression(initializer, out var left))
        {
            value = left.WithoutTrivia();
        }
        else
        {
            return false;
        }

        output.Add(LoweringSyntax.DeclareLocal(Name(variable, IrTemporaryKind.Condition, origin), value));
        return true;
    }

    // if (<cond'>) { ...; t = <when true'>; } else { ...; t = <when false'>; }
    // if (t) { ...; t = <right'>; }     (if (!t) for ||)
    private bool TryPrintConditional(IrIf @if, ImmutableArray<StatementSyntax>.Builder output)
    {
        ExpressionSyntax condition;
        if (@if.Else is null)
        {
            if (!TryGuard(@if.Condition, out var guard))
            {
                return false;
            }

            condition = guard;
        }
        else if (TryPrintExpression(@if.Condition, out var printed))
        {
            condition = printed.WithoutTrivia();
        }
        else
        {
            return false;
        }

        if (!TryPrintBranch(@if.Then, out var then))
        {
            return false;
        }

        if (@if.Else is null)
        {
            output.Add(SyntaxFactory.IfStatement(condition, then));
            return true;
        }

        if (!TryPrintBranch(@if.Else, out var @else))
        {
            return false;
        }

        output.Add(SyntaxFactory.IfStatement(condition, then, SyntaxFactory.ElseClause(@else)));
        return true;
    }

    // t or !t, the temporary of && or || holding the left operand
    private bool TryGuard(IrExpression condition, [NotNullWhen(true)] out ExpressionSyntax? guard)
    {
        guard = condition switch
        {
            IrVariableReference reference when _temporaries.TryGetValue(reference.Variable, out var temporary) =>
                SyntaxFactory.IdentifierName(temporary.Name),
            IrUnary { Operand: IrVariableReference reference } when
                _temporaries.TryGetValue(reference.Variable, out var temporary) =>
                SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression,
                    SyntaxFactory.IdentifierName(temporary.Name)),
            _ => null
        };
        return guard is not null;
    }

    private bool TryPrintBranch(IrStatement branch, [NotNullWhen(true)] out BlockSyntax? block)
    {
        block = null;
        if (branch is not IrBlock irBlock)
        {
            return false;
        }

        var statements = ImmutableArray.CreateBuilder<StatementSyntax>(irBlock.Statements.Length);
        foreach (var statement in irBlock.Statements)
        {
            if (!TryPrintStep(statement, statements))
            {
                return false;
            }
        }

        block = SyntaxFactory.Block(statements);
        return true;
    }

    // t = <value'>; the value of a branch of a conditional
    private bool TryAssignTemporary(IrAssignment assignment, ImmutableArray<StatementSyntax>.Builder output)
    {
        if (assignment.Target is not IrVariableReference target ||
            !_temporaries.TryGetValue(target.Variable, out var temporary) ||
            !TryPrintExpression(assignment.Value, out var value))
        {
            return false;
        }

        output.Add(Assign(SyntaxFactory.IdentifierName(temporary.Name), value));
        return true;
    }

    // { var t = <location'>; }: the location is evaluated for its checks only
    private bool TryPrintCheck(IrAssignment check, ImmutableArray<StatementSyntax>.Builder output)
    {
        if (!index.TryGet(check.Value.Source, out var origin) || !TryPrintExpression(check.Value, out var location))
        {
            return false;
        }

        var declaration = LoweringSyntax.DeclareTemporary(
            names.NextArgument(),
            location.WithoutTrivia().WithSpilledTypeSpanFrom(origin, compilation),
            origin,
            compilation,
            new SyntaxAnnotation(LoweringAnnotations.ArgumentTempAnnotationKind)
        );
        output.Add(SyntaxFactory.Block(declaration));
        return true;
    }

    // t op= <value'>; a copy of the compound assignment with its target replaced by the temporary
    private bool TryApplyCompound(IrCompoundAssignment compound, ImmutableArray<StatementSyntax>.Builder output)
    {
        if (compound.Target is not IrVariableReference target ||
            !_temporaries.TryGetValue(target.Variable, out var temporary) ||
            !index.TryGet(compound.Source, out var syntax) || syntax is not AssignmentExpressionSyntax assignment ||
            !TryCollect(assignment, compound.Value, out var replacements) ||
            replacements.ContainsKey(assignment.Left))
        {
            return false;
        }

        replacements[assignment.Left] = Reference(temporary);
        if (Replace(assignment, replacements) is not ExpressionSyntax applied)
        {
            return false;
        }

        output.Add(SyntaxFactory.ExpressionStatement(applied.WithoutTrivia()));
        return true;
    }

    // <target'> = t;
    private bool TryStoreCompound(IrAssignment store, IrVariableReference value,
        ImmutableArray<StatementSyntax>.Builder output)
    {
        if (!_temporaries.TryGetValue(value.Variable, out var temporary) ||
            !TryPrintExpression(store.Target, out var target))
        {
            return false;
        }

        output.Add(SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                target.WithoutTrivia(),
                Reference(temporary)
            )
        ));
        return true;
    }

    // <name> = <value>;
    private static ExpressionStatementSyntax Assign(IdentifierNameSyntax name, ExpressionSyntax value) =>
        SyntaxFactory.ExpressionStatement(
            SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, name, value.WithoutTrivia())
        );

    // [WHY] A call that splits declares its out locals before it: the callee writes them on its last resume
    private static InvocationExpressionSyntax DeclareOutArguments(InvocationExpressionSyntax call,
        ImmutableArray<StatementSyntax>.Builder output)
    {
        var arguments = call.ArgumentList.Arguments;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument.Expression is DeclarationExpressionSyntax
                {
                    Designation: SingleVariableDesignationSyntax designation
                } declaration)
            {
                var (statement, reference) = LoweringSyntax.DeclareOutVariable(declaration, designation);
                output.Add(statement);
                arguments = arguments.Replace(argument, argument.WithExpression(reference));
            }
        }

        return call.WithArgumentList(call.ArgumentList.WithArguments(arguments));
    }

    // The copy of the syntax of an expression, with the nodes the plan replaced read from their temporaries
    private bool TryPrintExpression(IrExpression expression, [NotNullWhen(true)] out ExpressionSyntax? printed)
    {
        printed = null;
        if (expression is IrVariableReference reference && _temporaries.TryGetValue(reference.Variable, out var temp))
        {
            printed = Reference(temp);
            return true;
        }

        if (!index.TryGet(expression.Source, out var syntax) || syntax is not ExpressionSyntax ||
            !TryReplaceTemporaries(syntax, expression, out var copy))
        {
            return false;
        }

        printed = copy as ExpressionSyntax;
        return printed is not null;
    }

    private bool TryReplaceTemporaries(SyntaxNode syntax, IrNode node, [NotNullWhen(true)] out SyntaxNode? copy)
    {
        copy = null;
        if (!TryCollect(syntax, node, out var replacements))
        {
            return false;
        }

        copy = Replace(syntax, replacements);
        return true;
    }

    private static SyntaxNode Replace(SyntaxNode syntax, Dictionary<SyntaxNode, ExpressionSyntax> replacements) =>
        replacements.TryGetValue(syntax, out var whole)
            ? whole.WithTriviaFrom(syntax)
            : syntax.ReplaceNodes(replacements.Keys,
                (original, _) => replacements[original].WithTriviaFrom(original));

    // The syntax of every node below node that reads a temporary, each with the reference that replaces it; it lies in
    // syntax, found by the span of the node it replaced
    private bool TryCollect(SyntaxNode syntax, IrNode node,
        [NotNullWhen(true)] out Dictionary<SyntaxNode, ExpressionSyntax>? replacements)
    {
        replacements = [];
        foreach (var descendant in IrNodeChildren.DescendantsAndSelf(node))
        {
            if (descendant is not IrVariableReference { Variable.Kind: IrVariableKind.Temporary } reference)
            {
                continue;
            }

            if (!_temporaries.TryGetValue(reference.Variable, out var temporary) ||
                !index.TryGet(reference.Source, out var replaced) ||
                !replaced.AncestorsAndSelf().AsStackEnumerable().Any(syntax, static (n, s) => n == s) ||
                (replacements.TryGetValue(replaced, out var other) &&
                 other is IdentifierNameSyntax { Identifier.ValueText: var name } && name != temporary.Name))
            {
                replacements = null;
                return false;
            }

            replacements[OutermostParentheses(replaced, syntax)] = Reference(temporary);
        }

        return true;
    }

    // [WHY] An operation has the syntax inside the parentheses written around it; the syntax passes spill and replace
    // the parenthesized expression, so the temporary takes the parentheses with it as they do
    private static SyntaxNode OutermostParentheses(SyntaxNode node, SyntaxNode within)
    {
        while (node != within && node.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            node = parenthesized;
        }

        return node;
    }

    // value in the parentheses written around origin, each a copy that keeps its original span
    private static ExpressionSyntax WithParentheses(SyntaxNode origin, ExpressionSyntax value)
    {
        for (var parent = origin.Parent; parent is ParenthesizedExpressionSyntax parenthesized; parent = parent.Parent)
        {
            value = parenthesized.WithExpression(value);
        }

        return value;
    }

    // A temporary of an operand, a warp operation or a call carries the spilled type span of what it holds; the one of
    // a conditional carries nothing, as ConditionalEvaluationRewriter writes it
    private ExpressionSyntax Reference(Temporary temporary)
    {
        var name = SyntaxFactory.IdentifierName(temporary.Name);
        return temporary.Kind == IrTemporaryKind.Condition
            ? name
            : name.WithSpilledTypeSpanFrom(temporary.Origin, compilation);
    }

    private string Name(IrVariable variable, IrTemporaryKind kind, SyntaxNode origin)
    {
        var name = names.Next(kind);
        _temporaries[variable] = new Temporary(name, kind, origin);
        return name;
    }

    // The name of a temporary, what it holds and the syntax of the node whose value it holds
    private readonly record struct Temporary(string Name, IrTemporaryKind Kind, SyntaxNode Origin);
}
