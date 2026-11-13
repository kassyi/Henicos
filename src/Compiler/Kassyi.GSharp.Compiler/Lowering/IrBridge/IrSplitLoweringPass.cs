using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Writes out, on the IR, the statements of a prepared body whose split plan the IR decides (stage 1 of #1561): every
/// operand C# evaluates before a split goes into a temporary, in the order C# evaluates it, and every split goes to the
/// top of a statement. The statements written are marked with
/// <see cref="LoweringAnnotations.IrLoweredAnnotationKind"/>, which the syntax-based passes that do the same
/// (<see cref="ConditionalEvaluationRewriter"/>, <see cref="WarpInstructionRewriter"/>) leave as they are; every other
/// statement stays to them.
/// </summary>
/// <remarks>
/// The unit is one statement of the source, reached through blocks and the branches of an <see langword="if"/> (the
/// statements of a loop, a switch or a try are inside opaque nodes of the IR). A statement is written out when all of
/// these hold: the plan of each of its IR statements has no fallback; the prepared statement is the statement of the
/// source (no expansion, removal or mutant switch changed it); the plan can be applied
/// (<see cref="IrSpillRewriter"/>); every operand evaluated into a <c>var</c> temporary can be copied out of its
/// statement (<see cref="IrOperandCopies"/>); the split annotations of the syntax agree with the splits of the IR,
/// before and after the statement is written (<see cref="IrSplitAnnotationCheck"/>); and every node has syntax to copy
/// (<see cref="IrLoweredSyntaxPrinter"/>). The reason for each other statement is kept in <see cref="Outcomes"/>.
/// </remarks>
/// <param name="semanticModel">A semantic model of the tree of <paramref name="body"/>.</param>
/// <param name="owner">The kernel or device function that declares <paramref name="body"/>.</param>
/// <param name="body">The body as written, in a tree of the compilation.</param>
/// <param name="names">The names of the temporaries of the method, shared with the syntax passes.</param>
internal sealed class IrSplitLoweringPass(SemanticModel semanticModel, IMethodSymbol owner, CSharpSyntaxNode body,
    LoweringTempNames names) : IBlockLoweringPass
{
    private static readonly SyntaxAnnotation s_irLowered = new(LoweringAnnotations.IrLoweredAnnotationKind);

    private readonly SemanticModel _semanticModel = semanticModel;
    private readonly LoweringTempNames _names = names;
    private readonly ImmutableArray<IrSplitLoweringOutcome>.Builder _outcomes =
        ImmutableArray.CreateBuilder<IrSplitLoweringOutcome>();

    /// <summary>What was done with each statement of the source whose IR statements hold a split.</summary>
    public IReadOnlyList<IrSplitLoweringOutcome> Outcomes => _outcomes;

    /// <summary>Writes out the statements of the prepared body <paramref name="block"/> that the IR plans.</summary>
    public BlockSyntax Rewrite(BlockSyntax block)
    {
        if (IrSplitStatements.TryCreate(_semanticModel, owner, body, block) is not { } statements)
        {
            return block;
        }

        return new Writer(this, statements, CountSources(block, statements.TreeOrdinal)).RewriteBlock(block);
    }

    // How many statements of the prepared body stand for each statement of the source: more than one after an expansion
    private static Dictionary<TextSpan, int> CountSources(BlockSyntax block, int treeOrdinal)
    {
        var counts = new Dictionary<TextSpan, int>();
        foreach (var node in block.DescendantNodes())
        {
            if (node is StatementSyntax && IrSyntaxIndex.OriginalSpanOf(node, treeOrdinal) is { } span)
            {
                counts[span] = counts.TryGetValue(span, out var count) ? count + 1 : 1;
            }
        }

        return counts;
    }

    // Walks the blocks and the branches of the ifs of one body
    private sealed class Writer(IrSplitLoweringPass pass, IrSplitStatements statements,
        Dictionary<TextSpan, int> sourceCounts)
    {
        public BlockSyntax RewriteBlock(BlockSyntax block)
        {
            var rewritten = ImmutableArray.CreateBuilder<StatementSyntax>(block.Statements.Count);
            foreach (var statement in block.Statements)
            {
                rewritten.AddRange(RewriteStatement(statement, out _));
            }

            return block.WithStatements(SyntaxFactory.List(rewritten));
        }

        // The statements that replace statement: what the IR wrote, or statement with its nested statements rewritten
        private ImmutableArray<StatementSyntax>.Builder RewriteStatement(StatementSyntax statement, out bool lowered)
        {
            lowered = false;
            if (IrSyntaxIndex.OriginalSpanOf(statement, statements.TreeOrdinal) is { } source &&
                statements.TryGetPlanned(source, out var irStatements))
            {
                var outcome = Lower(statement, source, irStatements, out var remainingIf);
                pass._outcomes.Add(outcome);
                if (outcome.IsLowered)
                {
                    lowered = true;
                    // [WHY] An if whose condition the IR wrote keeps its branches, whose statements have plans of
                    // their own; the if itself is not marked, and its condition holds no split any more
                    var printed = ImmutableArray.CreateBuilder<StatementSyntax>(outcome.Printed.Length);
                    foreach (var written in outcome.Printed)
                    {
                        printed.Add(written == remainingIf
                            ? Descend(written)
                            : written.WithAdditionalAnnotations(s_irLowered));
                    }

                    return printed;
                }
            }

            var unchanged = ImmutableArray.CreateBuilder<StatementSyntax>(1);
            unchanged.Add(Descend(statement));
            return unchanged;
        }

        private StatementSyntax Descend(StatementSyntax statement) => statement switch
        {
            BlockSyntax block => RewriteBlock(block),
            IfStatementSyntax @if => @if
                .WithStatement(RewriteEmbedded(@if.Statement))
                .WithElse(@if.Else?.WithStatement(RewriteEmbedded(@if.Else.Statement))),
            _ => statement
        };

        // A branch without braces gets a block when the IR writes it out
        private StatementSyntax RewriteEmbedded(StatementSyntax statement)
        {
            if (statement is BlockSyntax block)
            {
                return RewriteBlock(block);
            }

            var rewritten = RewriteStatement(statement, out var lowered);
            return lowered ? SyntaxFactory.Block(rewritten) : rewritten[0];
        }

        private IrSplitLoweringOutcome Lower(StatementSyntax statement, TextSpan source,
            ImmutableArray<IrStatement>.Builder irStatements, out IfStatementSyntax? remainingIf)
        {
            remainingIf = null;
            var plans = Plans(irStatements);
            foreach (var plan in plans)
            {
                if (plan.Fallback != IrSpillFallback.None)
                {
                    return Left(source, plan.Fallback, IrSpillApplySkip.None, plans);
                }
            }

            var skip = Check(statement, source, irStatements, plans, out var lowered);
            if (skip != IrSpillApplySkip.None)
            {
                return Left(source, IrSpillFallback.None, skip, plans);
            }

            var index = IrSyntaxIndex.Build(statement, statements.TreeOrdinal);
            if (!plans
                    .AsStackEnumerable()
                    .All(index, static (plan, syntax) => IrSplitAnnotationCheck.SplitsAreAnnotated(plan.Steps, syntax)))
            {
                return Left(source, IrSpillFallback.None, IrSpillApplySkip.SplitAnnotationMismatch, plans);
            }

            // [WHY] The names are taken from a copy and kept only when the whole statement is written, so a statement
            // left to the syntax passes takes no number
            var fork = pass._names.Fork();
            var printer = new IrLoweredSyntaxPrinter(index, fork, pass._semanticModel.Compilation);
            var output = ImmutableArray.CreateBuilder<StatementSyntax>();
            if (!TryPrint(statement, irStatements, lowered, printer, output, out remainingIf))
            {
                remainingIf = null;
                return Left(source, IrSpillFallback.None, IrSpillApplySkip.NotMapped, plans);
            }

            if (!output
                    .AsStackEnumerable()
                    .All(remainingIf, static (printed, @if) =>
                        IrSplitAnnotationCheck.SplitsAreAtTheTop(printed, conditionOnly: printed == @if)))
            {
                remainingIf = null;
                return Left(source, IrSpillFallback.None, IrSpillApplySkip.SplitAnnotationMismatch, plans);
            }

            pass._names.Adopt(fork);
            return new IrSplitLoweringOutcome(source, IrSpillFallback.None, IrSpillApplySkip.None, plans,
                output.ToImmutableArray());
        }

        private ImmutableArray<IrStatementSpillPlan> Plans(ImmutableArray<IrStatement>.Builder irStatements)
        {
            var plans = ImmutableArray.CreateBuilder<IrStatementSpillPlan>(irStatements.Count);
            foreach (var irStatement in irStatements)
            {
                if (statements.TryGetPlan(irStatement, out var plan))
                {
                    plans.Add(plan);
                }
            }

            return plans.ToImmutable();
        }

        // The conditions that do not need the output: the prepared statement, the rewrite and the operand types
        private IrSpillApplySkip Check(StatementSyntax statement, TextSpan source,
            ImmutableArray<IrStatement>.Builder irStatements, ImmutableArray<IrStatementSpillPlan> plans,
            out Dictionary<IrStatement, IrLoweredStatement> lowered)
        {
            lowered = [];
            if ((sourceCounts.TryGetValue(source, out var count) && count != 1) ||
                !IsSourceStatement(statement, statements.SourceOf(source)))
            {
                return IrSpillApplySkip.PreparedSyntaxDiffers;
            }

            if (irStatements.Count > 1 &&
                (statement is not LocalDeclarationStatementSyntax declaration ||
                 declaration.Declaration.Variables.Count != irStatements.Count ||
                 !irStatements
                     .AsStackEnumerable()
                     .All(static irStatement => irStatement is IrLocalDeclaration)))
            {
                return IrSpillApplySkip.NotMapped;
            }

            foreach (var plan in plans)
            {
                var application = IrSpillRewriter.Apply(plan);
                if (!application.TryGetLowered(out var loweredStatement))
                {
                    return application.Skip;
                }

                lowered[plan.Statement] = loweredStatement;
            }

            foreach (var plan in plans)
            {
                var skip = IrOperandCopies.Check(plan.Steps, statements.Root, pass._semanticModel);
                if (skip != IrSpillApplySkip.None)
                {
                    return skip;
                }
            }

            return IrSpillApplySkip.None;
        }

        private static bool TryPrint(StatementSyntax statement, ImmutableArray<IrStatement>.Builder irStatements,
            Dictionary<IrStatement, IrLoweredStatement> lowered, IrLoweredSyntaxPrinter printer,
            ImmutableArray<StatementSyntax>.Builder output, out IfStatementSyntax? remainingIf)
        {
            remainingIf = null;
            foreach (var irStatement in irStatements)
            {
                if (lowered.TryGetValue(irStatement, out var loweredStatement))
                {
                    if (!printer.TryPrint(loweredStatement, statement, output, out var remaining))
                    {
                        return false;
                    }

                    remainingIf = irStatement is IrIf ? remaining as IfStatementSyntax : null;
                }
                else if (irStatement is IrLocalDeclaration local &&
                         statement is LocalDeclarationStatementSyntax declaration &&
                         printer.TryPrintDeclarator(local, declaration, out var printed))
                {
                    output.Add(printed);
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        // [WHY] One structural comparison covers every pass that changes a statement before this one: the expansions,
        // the removal of calls C# removes and of Stryker's switches. An expression body is the synthesized statement
        // of its expression.
        private static bool IsSourceStatement(StatementSyntax prepared, SyntaxNode source) => source switch
        {
            ArrowExpressionClauseSyntax arrow => prepared switch
            {
                ReturnStatementSyntax { Expression: { } returned } => returned.IsEquivalentTo(arrow.Expression, false),
                ExpressionStatementSyntax expression => expression.Expression.IsEquivalentTo(arrow.Expression, false),
                _ => false
            },
            _ => prepared.IsEquivalentTo(source, topLevel: false)
        };

        private static IrSplitLoweringOutcome Left(TextSpan source, IrSpillFallback fallback, IrSpillApplySkip skip,
            ImmutableArray<IrStatementSpillPlan> plans) =>
            new(source, fallback, skip, plans, ImmutableArray<StatementSyntax>.Empty);
    }
}
