using Microsoft.CodeAnalysis.Text;

namespace Kassyi.GSharp.Compiler.Lowering;

public class ContextHoister : CSharpSyntaxRewriter
{
    private readonly HashSet<string> _hoistedVariables = [];

    public (BlockSyntax RewrittenBlock, IReadOnlyList<VariableDeclaratorSyntax> HoistedVariables) Hoist(
        BlockSyntax block)
    {
        _hoistedVariables.Clear();
        var hoistedDecls = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();

        // One-pass AST traversal to collect barriers and variables efficiently. (O(N))
        var analyzer = new HoistAnalyzer();
        analyzer.Visit(block);

        if (analyzer.BarrierSpans.Count == 0)
        {
            return (block, hoistedDecls);
        }

        var barrierCount = analyzer.BarrierSpans.Count;
        var barrierStarts = new int[barrierCount];
        for (var i = 0; i < barrierCount; i++)
        {
            barrierStarts[i] = analyzer.BarrierSpans[i].Start;
        }

        // Determine which variables cross a barrier using Binary Search. (O(V log B))
        foreach (var (decl, scopeEnd) in analyzer.Variables)
        {
            var declEnd = decl.Span.End;

            // Binary search for the first barrier that comes after the declaration.
            var index = Array.BinarySearch(barrierStarts, declEnd);
            if (index < 0)
            {
                index = ~index;
            }

            // If a barrier is found after the declaration and before the end of the scope, it crosses.
            if (index < barrierCount && barrierStarts[index] < scopeEnd)
            {
                _hoistedVariables.Add(decl.Identifier.ValueText);
                hoistedDecls.Add(decl);
            }
        }

        var rewrittenBlock = (BlockSyntax)Visit(block);
        return (rewrittenBlock, hoistedDecls);
    }

    public override SyntaxNode? VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        var remainingVariables = ImmutableArray.CreateBuilder<VariableDeclaratorSyntax>();
        var assignments = ImmutableArray.CreateBuilder<ExpressionStatementSyntax>();

        foreach (var variable in node.Declaration.Variables)
        {
            if (_hoistedVariables.Contains(variable.Identifier.ValueText))
            {
                // Hoisted variables are declared in the context struct. 
                // We must convert their local initializations into explicit assignments.
                if (variable.Initializer == null)
                {
                    continue;
                }

                var assignment = SyntaxFactory.AssignmentExpression(
                    SyntaxKind.SimpleAssignmentExpression,
                    CreateContextAccess(variable.Identifier.ValueText),
                    (ExpressionSyntax)Visit(variable.Initializer.Value)
                );
                assignments.Add(SyntaxFactory.ExpressionStatement(assignment));
            }
            else
            {
                var visitedVar = (VariableDeclaratorSyntax)base.VisitVariableDeclarator(variable)!;
                remainingVariables.Add(visitedVar);
            }
        }

        if (remainingVariables.Count == 0)
        {
            return assignments.Count switch
            {
                0 => null,
                1 => assignments[0],
                _ => SyntaxFactory.Block(assignments)
            };
        }

        var newDeclaration = node.Declaration.WithVariables(SyntaxFactory.SeparatedList(remainingVariables));
        var newLocalDecl = node.WithDeclaration(newDeclaration);

        if (assignments.Count == 0)
        {
            return newLocalDecl;
        }

        // If both hoisted and unhoisted variables existed in the same declaration, 
        // we wrap the separated assignments and remaining local declarations into a block 
        // to preserve C# syntax validity.
        var statements = ImmutableArray.CreateBuilder<StatementSyntax>();
        statements.Add(newLocalDecl);
        statements.AddRange(assignments);
        return SyntaxFactory.Block(statements);
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_hoistedVariables.Contains(node.Identifier.ValueText))
        {
            return CreateContextAccess(node.Identifier.ValueText).WithTriviaFrom(node);
        }

        return base.VisitIdentifierName(node);
    }

    private static MemberAccessExpressionSyntax CreateContextAccess(string variableName)
    {
        return SyntaxFactory.MemberAccessExpression(
            SyntaxKind.PointerMemberAccessExpression,
            SyntaxFactory.IdentifierName("_ctx"),
            SyntaxFactory.IdentifierName(variableName)
        );
    }

    private static bool IsSyncThreads(InvocationExpressionSyntax inv)
    {
        return inv.Expression switch
        {
            MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText == "SyncThreads" &&
                                               (ma.Expression as IdentifierNameSyntax)?.Identifier.ValueText == "Gpu",
            IdentifierNameSyntax id => id.Identifier.ValueText == "SyncThreads",
            _ => false
        };
    }

    private sealed class HoistAnalyzer : CSharpSyntaxWalker
    {
        public readonly ImmutableArray<TextSpan>.Builder BarrierSpans = ImmutableArray.CreateBuilder<TextSpan>();

        public readonly ImmutableArray<(VariableDeclaratorSyntax Decl, int ScopeEnd)>.Builder Variables =
            ImmutableArray.CreateBuilder<(VariableDeclaratorSyntax, int)>();

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsSyncThreads(node))
            {
                BarrierSpans.Add(node.Span);
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            var parentBlock = node.FirstAncestorOrSelf<BlockSyntax>();
            if (parentBlock != null)
            {
                Variables.Add((node, parentBlock.Span.End));
            }

            base.VisitVariableDeclarator(node);
        }
    }
}
