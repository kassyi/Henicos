using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Kassyi.GSharp.Compiler.Transpiler;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Checks the invariants of a statement rewritten by <see cref="IrSpillRewriter"/>, for tests: each violation is one
/// line that starts with the number of the invariant (<c>R3 ...</c>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>R1: a call that splits is the whole of an expression statement or the initializer of a
/// temporary, nowhere else.</description></item>
/// <item><description>R2: each temporary is declared once, never read before its declaration, and a temporary of a
/// branch is not read outside it.</description></item>
/// <item><description>R3: each operand, call and compound assignment of a step is evaluated once: exactly one node of
/// the output stands for it.</description></item>
/// <item><description>R4: no <see cref="IrSequence"/>, and no opaque node that holds a split.</description></item>
/// <item><description>R5: the store of a compound assignment whose target was read comes before the output of every
/// step after it.</description></item>
/// </list>
/// The statements below an <see langword="if"/> of the original statement are statements of their own, with plans of
/// their own: only its condition is checked.
/// </remarks>
internal sealed class IrLoweredInvariants
{
    private readonly IrLoweredStatement _lowered;
    private readonly SyntaxTree _tree;
    private readonly ImmutableArray<string>.Builder _violations = ImmutableArray.CreateBuilder<string>();
    private readonly HashSet<IrVariable> _declared = [];

    private IrLoweredInvariants(IrLoweredStatement lowered, Compilation compilation)
    {
        _lowered = lowered;
        _tree = SemanticModelExtensions.GetSyntaxTrees(compilation)[lowered.Original.Source.TreeOrdinal];
    }

    /// <summary>The violations of R1 to R5 in <paramref name="lowered"/>, made from <paramref name="plan"/>.</summary>
    public static ImmutableArray<string> Verify(IrStatementSpillPlan plan, IrLoweredStatement lowered,
        Compilation compilation)
    {
        var checker = new IrLoweredInvariants(lowered, compilation);
        checker.CheckStatements(lowered.Statements, ImmutableStack<IrVariable>.Empty);
        checker.CheckDeclaredOnce();
        checker.CheckEvaluatedOnce(plan.Steps);
        checker.CheckStores(plan.Steps);
        return checker._violations.ToImmutable();
    }

    // R1, R2, R4 over a list of statements; scope: the temporaries visible here
    private ImmutableStack<IrVariable> CheckStatements(ImmutableArray<IrStatement> statements,
        ImmutableStack<IrVariable> scope)
    {
        foreach (var statement in statements)
        {
            scope = CheckStatement(statement, scope);
        }

        return scope;
    }

    private ImmutableStack<IrVariable> CheckStatement(IrStatement statement, ImmutableStack<IrVariable> scope)
    {
        switch (statement)
        {
            case IrLocalDeclaration { Variable.Kind: IrVariableKind.Temporary } declaration:
                CheckExpression(declaration.Initializer, scope, topLevel: true);
                if (!_declared.Add(declaration.Variable))
                {
                    Report("R2", $"{declaration.Variable.Name} is declared twice");
                }

                return scope.Push(declaration.Variable);
            case IrExpressionStatement expression:
                CheckExpression(expression.Expression, scope, topLevel: true);
                return scope;
            case IrIf { Source.Synthesized: false } @if:
                // The original if: its branches are statements of their own
                CheckExpression(@if.Condition, scope, topLevel: false);
                return scope;
            case IrIf @if:
                CheckExpression(@if.Condition, scope, topLevel: false);
                CheckStatement(@if.Then, scope);
                if (@if.Else is { } @else)
                {
                    CheckStatement(@else, scope);
                }

                return scope;
            case IrBlock block:
                CheckStatements(block.Statements, scope);
                return scope;
            case IrLocalDeclaration declaration:
                CheckExpression(declaration.Initializer, scope, topLevel: false);
                return scope;
            case IrReturn @return:
                CheckExpression(@return.Value, scope, topLevel: false);
                return scope;
            default:
                Report("R4", $"a statement of kind {statement.GetType().Name} is in the output");
                return scope;
        }
    }

    // topLevel: the expression may itself be a call that splits
    private void CheckExpression(IrExpression? expression, ImmutableStack<IrVariable> scope, bool topLevel)
    {
        if (expression is null)
        {
            return;
        }

        CheckNode(expression, scope, topLevel);
    }

    private void CheckNode(IrNode node, ImmutableStack<IrVariable> scope, bool topLevel)
    {
        switch (node)
        {
            case IrCall call when RemovedCalls.IsRemoved(call.Method, _tree):
                return;
            case IrCall { Classification.Split.IsSplit: true } when !topLevel:
                Report("R1", "a call that splits is inside an expression");
                break;
            case IrVariableReference { Variable.Kind: IrVariableKind.Temporary } reference
                when !IsInScope(scope, reference.Variable):
                Report("R2", $"{reference.Variable.Name} is read where it is not declared");
                break;
            case IrSequence:
                Report("R4", "a sequence is in the output");
                break;
            case IrOpaqueExpression { Split: IrOpaqueSplit.HoldsSplit }:
                Report("R4", "an opaque node that holds a split is in the output");
                break;
        }

        foreach (var child in IrNodeChildren.Of(node))
        {
            CheckNode(child, scope, topLevel: false);
        }
    }

    private static bool IsInScope(ImmutableStack<IrVariable> scope, IrVariable variable)
    {
        foreach (var visible in scope)
        {
            if (visible == variable)
            {
                return true;
            }
        }

        return false;
    }

    private void CheckDeclaredOnce()
    {
        foreach (var temporary in _lowered.Temporaries)
        {
            if (!_declared.Contains(temporary.Variable))
            {
                Report("R2", $"{temporary.Variable.Name} is never declared");
            }
        }
    }

    // R3: the node of a spilled operand, a call that splits and a compound assignment appears once; the read of a
    // compound target and a checked location are evaluated twice on purpose (read and store, check and access)
    private void CheckEvaluatedOnce(ImmutableArray<IrSpillStep> steps)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case IrSpillConditional conditional:
                    foreach (var branch in conditional.Branches)
                    {
                        CheckEvaluatedOnce(branch);
                    }

                    break;
                case IrSpillValue { Role: IrSpillRole.CompoundRead } or IrSpillCheck:
                    break;
                default:
                    var count = Count(step.Node);
                    if (count != 1)
                    {
                        Report("R3", $"the node at {Where(step.Node)} is evaluated {count} times");
                    }

                    break;
            }
        }
    }

    // The nodes of the output that stand for node: the node, or a copy with rewritten children
    private int Count(IrExpression node)
    {
        var count = 0;
        foreach (var statement in _lowered.Statements)
        {
            foreach (var output in IrNodeChildren.DescendantsAndSelf(statement))
            {
                if (output.GetType() == node.GetType() && output.Source == node.Source)
                {
                    count++;
                }
            }
        }

        return count;
    }

    // R5
    private void CheckStores(ImmutableArray<IrSpillStep> steps)
    {
        for (var index = 0; index < steps.Length; index++)
        {
            if (steps[index] is not IrSpillCompoundStore store)
            {
                continue;
            }

            var stored = IndexOfStore(store.Compound);
            if (stored < 0)
            {
                Report("R5", $"the compound assignment at {Where(store.Compound)} is not stored");
                continue;
            }

            for (var later = index + 1; later < steps.Length; later++)
            {
                var output = IndexOfOutput(steps[later]);
                if (output >= 0 && output < stored)
                {
                    Report("R5", $"the compound assignment at {Where(store.Compound)} is stored after a later step");
                }
            }
        }
    }

    // The statement that stores the read temporary of compound in its target: <target> = t;
    private int IndexOfStore(IrCompoundAssignment compound)
    {
        var temporary = TemporaryOf(compound.Target);
        for (var index = 0; index < _lowered.Statements.Length; index++)
        {
            if (temporary is not null && _lowered.Statements[index] is IrExpressionStatement
                {
                    Expression: IrAssignment
                    {
                        Target: not IrVariableReference { Variable.Kind: IrVariableKind.Temporary },
                        Value: IrVariableReference read
                    }
                } && read.Variable == temporary)
            {
                return index;
            }
        }

        return -1;
    }

    // The top-level statement a step wrote: the declaration of its temporary, or its check; -1 when it wrote none
    private int IndexOfOutput(IrSpillStep step)
    {
        var temporary = TemporaryOf(step.Node);
        for (var index = 0; index < _lowered.Statements.Length; index++)
        {
            var statement = _lowered.Statements[index];
            var wrote = step is IrSpillCheck
                ? IsCheckOf(statement, step.Node)
                : statement is IrLocalDeclaration declaration && temporary is not null &&
                  declaration.Variable == temporary;
            if (wrote)
            {
                return index;
            }
        }

        return -1;
    }

    // _ = <location>;
    private static bool IsCheckOf(IrStatement statement, IrExpression location) =>
        statement is IrExpressionStatement { Expression: IrAssignment { Target: IrDiscard, Value: var value } } &&
        value.Source == location.Source;

    private IrVariable? TemporaryOf(IrExpression origin) =>
        _lowered.Temporaries
            .AsStackEnumerable()
            .Where(origin, static (temporary, node) => temporary.Origin == node)
            .Select(static temporary => temporary.Variable)
            .FirstOrDefault();

    private static string Where(IrNode node) => $"{node.Source.Start}..{node.Source.Start + node.Source.Length}";

    private void Report(string number, string text) =>
        _violations.Add($"{number} at {Where(_lowered.Original)}: {text}");
}
