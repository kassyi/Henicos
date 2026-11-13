using Kassyi.GSharp.Compiler.Ir;
using Kassyi.GSharp.Compiler.Ir.Building;
using Kassyi.GSharp.Compiler.Ir.Classifying;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;

namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// The statements of the IR of one method by the statement of the source they belong to, with the split plan of each
/// one that holds a split (<see cref="IrSplitLoweringPass"/>). A declaration of several locals has one IR statement
/// per local; an expression body has one, which belongs to the expression body.
/// </summary>
internal sealed class IrSplitStatements
{
    private readonly Dictionary<TextSpan, ImmutableArray<IrStatement>.Builder> _bySource = [];
    private readonly Dictionary<TextSpan, SyntaxNode> _sources = [];
    private readonly Dictionary<IrStatement, IrStatementSpillPlan> _plans = [];

    private IrSplitStatements(SyntaxNode root, int treeOrdinal)
    {
        Root = root;
        TreeOrdinal = treeOrdinal;
    }

    /// <summary>The root of the tree of the method in the compilation.</summary>
    public SyntaxNode Root { get; }

    /// <summary>The ordinal of the tree of the method in the compilation.</summary>
    public int TreeOrdinal { get; }

    /// <summary>
    /// Builds, classifies and plans the IR of <paramref name="owner"/>; <see langword="null"/> when
    /// <paramref name="body"/> is not the body of its declaration (a body built by a test) or when no split point of
    /// <paramref name="prepared"/> can be planned on the IR.
    /// </summary>
    /// <param name="model">A semantic model of the tree of <paramref name="body"/>.</param>
    /// <param name="owner">The method that declares <paramref name="body"/>.</param>
    /// <param name="body">The body as written, in a tree of the compilation.</param>
    /// <param name="prepared">The body after its preparation, with its split points annotated.</param>
    public static IrSplitStatements? TryCreate(SemanticModel model, IMethodSymbol owner, CSharpSyntaxNode body,
        BlockSyntax prepared)
    {
        var compilation = model.Compilation;
        if (!owner.TryGetDeclaringSyntax<SyntaxNode>(out var declaration) || body.Parent != declaration ||
            !compilation.ContainsSyntaxTree(body.SyntaxTree) || !HasPlannableSplit(prepared))
        {
            return null;
        }

        if (IrBuilder.TryBuild(owner, compilation, CancellationToken.None) is not { } built)
        {
            return null;
        }

        // [WHY] The plans refer to the statements of the classified method, a new tree
        var classified = IrClassifier.Classify(built, compilation);
        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(compilation, body.SyntaxTree);
        var statements = new IrSplitStatements(body.SyntaxTree.GetRoot(), treeOrdinal);
        statements.Add(classified.Body);
        foreach (var plan in IrSpillPlanner.Plan(classified, compilation).Statements)
        {
            statements._plans[plan.Statement] = plan;
        }

        return statements;
    }

    /// <summary>
    /// The IR statements of the source statement whose span is <paramref name="source"/>, in the order they run, when
    /// one of them has a plan.
    /// </summary>
    public bool TryGetPlanned(TextSpan source,
        [NotNullWhen(true)] out ImmutableArray<IrStatement>.Builder? statements) =>
        _bySource.TryGetValue(source, out statements) &&
        statements
            .AsStackEnumerable()
            .Any(_plans, static (statement, plans) => plans.ContainsKey(statement));

    /// <summary>The plan of <paramref name="statement"/>, when it holds a split.</summary>
    public bool TryGetPlan(IrStatement statement, [NotNullWhen(true)] out IrStatementSpillPlan? plan) =>
        _plans.TryGetValue(statement, out plan);

    /// <summary>The statement of the source, or the expression body, whose span is <paramref name="source"/>.</summary>
    public SyntaxNode SourceOf(TextSpan source) => _sources[source];

    /// <summary>
    /// The innermost statement of <paramref name="root"/> that holds <paramref name="span"/>, or the expression body
    /// that does: the statement of the source an IR statement belongs to.
    /// </summary>
    public static SyntaxNode StatementOf(SyntaxNode root, TextSpan span)
    {
        foreach (var node in root.FindNode(span, getInnermostNodeForTie: true).AncestorsAndSelf())
        {
            if (node is ArrowExpressionClauseSyntax ||
                (node is StatementSyntax && (node is not BlockSyntax || node.Span == span)))
            {
                return node;
            }
        }

        throw new InvalidOperationException($"No statement holds the span {span}.");
    }

    // A block and the branches of an if hold statements of their own, as the planner reads them
    private void Add(IrStatement statement)
    {
        switch (statement)
        {
            case IrBlock block:
                foreach (var inner in block.Statements)
                {
                    Add(inner);
                }

                return;
            case IrIf @if:
                AddOwn(@if);
                Add(@if.Then);
                if (@if.Else is { } @else)
                {
                    Add(@else);
                }

                return;
            default:
                AddOwn(statement);
                return;
        }
    }

    private void AddOwn(IrStatement statement)
    {
        var source = StatementOf(Root, statement.Source.Span);
        if (!_bySource.TryGetValue(source.Span, out var list))
        {
            list = ImmutableArray.CreateBuilder<IrStatement>();
            _bySource[source.Span] = list;
            _sources[source.Span] = source;
        }

        list.Add(statement);
    }

    // [WHY] Only a statement reached through blocks and ifs is a statement of the IR: a split point inside a loop, a
    // switch, a try or a lambda is inside an opaque node, so a method without any other one has nothing to plan
    private static bool HasPlannableSplit(BlockSyntax prepared) =>
        prepared.DescendantNodes()
            .AsStackEnumerable()
            .Any(prepared, static (node, body) =>
                node is InvocationExpressionSyntax invocation &&
                BarrierHelper.IsPhaseBarrier(invocation) &&
                IsReachedThroughBlocksAndIfs(invocation, body)
            );

    private static bool IsReachedThroughBlocksAndIfs(SyntaxNode node, BlockSyntax body)
    {
        for (var current = node.Parent; current != null && current != body; current = current.Parent)
        {
            if (current is StatementSyntax and not (BlockSyntax or IfStatementSyntax or ExpressionStatementSyntax
                    or LocalDeclarationStatementSyntax or ReturnStatementSyntax) ||
                current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
            {
                return false;
            }
        }

        return true;
    }
}
