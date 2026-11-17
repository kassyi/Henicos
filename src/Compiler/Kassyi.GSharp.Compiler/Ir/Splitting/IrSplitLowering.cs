using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Ir.Splitting;

/// <summary>
/// Applies the split plans of a whole method body on the IR for the state machine pass. Unlike IrSplitLoweringPass,
/// which writes single statements back to syntax for the syntax lowering, it keeps the result as IR.
/// </summary>
internal static class IrSplitLowering
{
    /// <summary>
    /// The body with the split plan of every statement applied; <see langword="null"/> when a statement cannot be
    /// (<paramref name="failure"/> says why).
    /// </summary>
    public static IrSplitLoweredBody? Apply(IrMethod method, Compilation compilation,
        out IrSplitLoweringFailure failure)
    {
        var plan = IrSpillPlanner.Plan(method, compilation);
        foreach (var statementPlan in plan.Statements)
        {
            if (statementPlan.Fallback != IrSpillFallback.None)
            {
                failure = new IrSplitLoweringFailure(IrSplitLoweringFailureKind.Fallback, statementPlan.Fallback,
                    IrSpillApplySkip.None, statementPlan.Statement.Source);
                return null;
            }
        }

        if (FindFirstOpaqueStatement(method.Body) is { } opaque)
        {
            failure = new IrSplitLoweringFailure(IrSplitLoweringFailureKind.OpaqueStatement, IrSpillFallback.None,
                IrSpillApplySkip.None, opaque.Source);
            return null;
        }

        var loweredStatements = new Dictionary<IrStatement, IrLoweredStatement>();
        var originalLoopConditions = new Dictionary<IrWhile, IrLoweredCondition>();
        foreach (var statementPlan in plan.Statements)
        {
            if (statementPlan.Statement is IrWhile @while)
            {
                if (!IrSpillRewriter.TryApplyLoopCondition(statementPlan, out var loweredCondition, out var skip))
                {
                    failure = new IrSplitLoweringFailure(IrSplitLoweringFailureKind.Skip, IrSpillFallback.None, skip,
                        statementPlan.Statement.Source);
                    return null;
                }

                originalLoopConditions[@while] = loweredCondition;
            }
            else
            {
                var application = IrSpillRewriter.Apply(statementPlan);
                if (!application.TryGetLowered(out var loweredStatement))
                {
                    failure = new IrSplitLoweringFailure(IrSplitLoweringFailureKind.Skip, IrSpillFallback.None,
                        application.Skip, statementPlan.Statement.Source);
                    return null;
                }

                loweredStatements[statementPlan.Statement] = loweredStatement;
            }
        }

        var rewriter = new BodyRewriter(loweredStatements, originalLoopConditions);
        var rewrittenBody = rewriter.VisitBlock(method.Body);
        failure = default;
        return new IrSplitLoweredBody(rewrittenBody, rewriter.RebuiltLoopConditions.ToImmutable());
    }

    /// <summary>Whether <paramref name="node"/> holds a call that splits, not counting a call C# removes and what is
    /// below it.</summary>
    public static bool HoldsSplit(IrNode node, Compilation compilation)
    {
        var trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
        return HoldsSplit(node, trees);
    }

    private static bool HoldsSplit(IrNode node, SyntaxTree[] trees)
    {
        switch (node)
        {
            case IrCall call when RemovedCalls.IsRemoved(call.Method, trees[call.Source.TreeOrdinal]):
                return false;
            case IrCall call:
                if (call.Classification.Split.IsSplit)
                {
                    return true;
                }

                break;
            case IrOpaqueExpression opaque:
                return opaque.Split switch
                {
                    IrOpaqueSplit.HoldsSplit => true,
                    IrOpaqueSplit.None => false,
                    _ => throw new InvalidOperationException("The opaque node has not been classified.")
                };
            case IrOpaqueStatement opaque:
                return opaque.Split switch
                {
                    IrOpaqueSplit.HoldsSplit => true,
                    IrOpaqueSplit.None => false,
                    _ => throw new InvalidOperationException("The opaque node has not been classified.")
                };
        }

        return IrNodeChildren.Of(node)
            .AsStackEnumerable()
            .Any(trees, static (child, syntaxTrees) => HoldsSplit(child, syntaxTrees));
    }

    private static IrOpaqueStatement? FindFirstOpaqueStatement(IrNode node)
    {
        if (node is IrOpaqueStatement opaque)
        {
            return opaque;
        }

        return IrNodeChildren.Of(node)
            .AsStackEnumerable()
            .Select(static child => FindFirstOpaqueStatement(child))
            .OfType<IrOpaqueStatement>()
            .FirstOrDefault();
    }

    private sealed class BodyRewriter(
        Dictionary<IrStatement, IrLoweredStatement> loweredStatements,
        Dictionary<IrWhile, IrLoweredCondition> originalLoopConditions) : IrRewriter
    {
        public ImmutableDictionary<IrWhile, IrLoweredCondition>.Builder RebuiltLoopConditions { get; } =
            ImmutableDictionary.CreateBuilder<IrWhile, IrLoweredCondition>();

        public override IrStatement VisitStatement(IrStatement statement)
        {
            switch (statement)
            {
                case IrWhile @while:
                {
                    var body = VisitStatement(@while.Body);
                    var rebuilt = @while.Update(@while.Condition, body);
                    if (originalLoopConditions.TryGetValue(@while, out var condition))
                    {
                        RebuiltLoopConditions.Add(rebuilt, condition);
                    }

                    return rebuilt;
                }
                case IrIf @if:
                {
                    var then = VisitStatement(@if.Then);
                    var @else = @if.Else is { } elseBranch ? VisitStatement(elseBranch) : null;
                    if (!loweredStatements.TryGetValue(@if, out var lowered))
                    {
                        return @if.Update(@if.Condition, then, @else);
                    }

                    var remainingIf = (IrIf)(lowered.Remaining ??
                        throw new InvalidOperationException("A split if statement has no remaining if."));
                    var updatedIf = remainingIf.Update(remainingIf.Condition, then, @else);
                    var statements = lowered.Statements.SetItem(lowered.Statements.Length - 1, updatedIf);
                    return Wrap(@if, statements);
                }
                default:
                    if (loweredStatements.TryGetValue(statement, out var loweredSimple))
                    {
                        return Wrap(statement, loweredSimple.Statements);
                    }

                    return base.VisitStatement(statement);
            }
        }

        private static IrStatement Wrap(IrStatement original, ImmutableArray<IrStatement> statements)
        {
            if (statements.Length == 1 && statements[0] == original)
            {
                return original;
            }

            var builder = ImmutableArray.CreateBuilder<IrStatement>(statements.Length);
            var locals = ImmutableArray.CreateBuilder<IrVariable>();
            for (var index = 0; index < statements.Length; index++)
            {
                var current = statements[index];
                var comments = index == 0 ? original.CommentsBefore : ImmutableArray<string>.Empty;
                var updated = IrStatement.WithCommentsBefore(current, comments);
                builder.Add(updated);
                if (updated is IrLocalDeclaration { Variable.Kind: IrVariableKind.Temporary } declaration)
                {
                    locals.Add(declaration.Variable);
                }
            }

            return new IrBlock(builder.MoveToImmutable(), locals.ToImmutable(), original.Source.AsSynthesized());
        }
    }
}
