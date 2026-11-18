using Kassyi.GSharp.Compiler.Ir.Expressions;
using Kassyi.GSharp.Compiler.Ir.Splitting;
using Kassyi.GSharp.Compiler.Ir.Statements;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Ir.Reconverging;

/// <summary>
/// Decides the <see cref="IrUniformity"/> of every <see cref="IrIf"/> and <see cref="IrWhile"/> in a split-lowered
/// body.
/// </summary>
internal static class IrUniformityClassifier
{
    /// <summary>
    /// Returns <paramref name="body"/> with the uniformity of every <see cref="IrIf"/> and <see cref="IrWhile"/> in
    /// <see cref="IrSplitLoweredBody.Body"/> decided.
    /// </summary>
    public static IrSplitLoweredBody Classify(IrMethod method, IrSplitLoweredBody body, Compilation compilation,
        bool isDeviceFunction, bool reachesActiveMask)
    {
        UniformityAnalysis? analysis = null;
        MethodDeclarationSyntax? declaration = null;
        if (reachesActiveMask &&
            method.Symbol.TryGetDeclaringSyntax(out MethodDeclarationSyntax? found) &&
            compilation.ContainsSyntaxTree(found.SyntaxTree))
        {
            declaration = found;
            var declarationModel = compilation.GetCachedSemanticModel(declaration.SyntaxTree);
            var masks = isDeviceFunction ? new ArgumentMasks(ulong.MaxValue, 0) : default;
            if (!new MethodFlowGraph(declarationModel, declaration).TryGetUniformity(masks, out analysis))
            {
                analysis = null;
            }
        }

        var table = new IrLoopConditionTable(body.LoopConditions);
        var trees = SemanticModelExtensions.GetSyntaxTrees(compilation);
        var classifier = new Classifier(table, trees, analysis, declaration, reachesActiveMask);
        var rewritten = classifier.VisitBlock(body.Body);
        return new IrSplitLoweredBody(rewritten, table.ToImmutable());
    }

    private sealed class Classifier(
        IrLoopConditionTable table,
        SyntaxTree[] trees,
        UniformityAnalysis? analysis,
        MethodDeclarationSyntax? declaration,
        bool reachesActiveMask) : IrRewriter
    {
        public override IrStatement VisitStatement(IrStatement statement)
        {
            var visited = base.VisitStatement(statement);
            switch (visited)
            {
                case IrIf @if:
                    return @if.WithUniformity(Decide(@if.Condition));
                case IrWhile @while:
                    table.Rebuilt((IrWhile)statement, @while);
                    var classified = @while.WithUniformity(Decide(@while.Condition));
                    table.Rebuilt(@while, classified);
                    return classified;
                case IrBlock or IrLocalDeclaration or IrExpressionStatement or IrReturn:
                    return visited;
                default:
                    throw new InvalidOperationException(
                        $"Cannot classify uniformity for statement of kind {visited.GetType().Name}.");
            }
        }

        private IrUniformity Decide(IrExpression condition)
        {
            if (!reachesActiveMask)
            {
                return IrUniformity.NotRequired;
            }

            if (analysis is null || declaration is null || IsTemporaryCondition(condition))
            {
                return IrUniformity.ThreadVariant;
            }

            if (condition.Source.Region != 0 ||
                (uint)condition.Source.TreeOrdinal >= (uint)trees.Length)
            {
                return IrUniformity.ThreadVariant;
            }

            var tree = trees[condition.Source.TreeOrdinal];
            if (!ReferenceEquals(tree, declaration.SyntaxTree) ||
                !declaration.Span.Contains(condition.Source.Span))
            {
                return IrUniformity.ThreadVariant;
            }

            var syntaxNode = tree.GetRoot().FindNode(condition.Source.Span);
            if (syntaxNode is not ExpressionSyntax expression || expression.Span != condition.Source.Span)
            {
                return IrUniformity.ThreadVariant;
            }

            return analysis.IsThreadVariant(expression) ? IrUniformity.ThreadVariant : IrUniformity.Uniform;
        }

        private static bool IsTemporaryCondition(IrExpression condition) =>
            condition is IrVariableReference { Variable.Kind: IrVariableKind.Temporary } or
                IrUnary
                {
                    Operator.Kind: UnaryOperatorKind.Not,
                    Source.Synthesized: true,
                    Operand: IrVariableReference { Variable.Kind: IrVariableKind.Temporary }
                };
    }
}
