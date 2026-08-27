using System.Collections.Concurrent;

namespace Kassyi.GSharp.Compiler.Transpiler;

public class RoslynToCudaVisitor : CSharpSyntaxWalker, IDisposable
{
    private static readonly ConcurrentBag<RoslynToCudaVisitor> s_pool = [];

    // Pooled Collections to achieve true zero-allocation during generation
    private readonly HashSet<INamedTypeSymbol> _usedStructs = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, OverrideValue> _localVariableOverrides = new();

    internal TranspileContext _context;
    private int _isRented;

    private RoslynToCudaVisitor()
    {
    }

    public static RoslynToCudaVisitor Rent(SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null, ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>? bclIntrinsicMap = null)
    {
        if (!s_pool.TryTake(out var visitor))
        {
            visitor = new RoslynToCudaVisitor();
        }

        Volatile.Write(ref visitor._isRented, 1);

        visitor._usedStructs.Clear();
        visitor._localVariableOverrides.Clear();

        visitor._context = new TranspileContext(
            default, // Writer is injected by MethodTranspiler
            visitor._usedStructs,
            visitor._localVariableOverrides,
            semanticModel,
            reportDiagnostic,
            visitor,
            bclIntrinsicMap ?? ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty);

        return visitor;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isRented, 0) != 1)
        {
            return;
        }

        _context = default;
        s_pool.Add(this);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
            node.Kind().ToString()));
        base.DefaultVisit(node);
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            _context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                node.GetLocation()));
        }

        base.VisitPredefinedType(node);
    }

    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            using (_context.Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    Visit(stmt);
                    _context.Writer.AppendLine();
                }
            }
        }
        else
        {
            // Auto-wrap single statements in braces to support C# bracket omission safely in C++
            using (_context.Writer.Scope())
            {
                Visit(statement);
                if (!statement.IsKind(SyntaxKind.Block))
                {
                    _context.Writer.AppendLine();
                }
            }
        }
    }

    // --- Statements ---

    public override void VisitBlock(BlockSyntax node)
    {
        foreach (var statement in node.Statements)
        {
            Visit(statement);
            _context.Writer.AppendLine();
        }
    }

    public override void VisitIfStatement(IfStatementSyntax node)
        => StatementTranspilers.TranspileIfStatement(node, ref _context);

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Visit(node.Declaration);
        _context.Writer.AppendSemiColon();
        _context.Writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        => VisitStatementOrBlock(node.Block);

    public override void VisitForStatement(ForStatementSyntax node)
        => StatementTranspilers.TranspileForStatement(node, ref _context);

    public override void VisitWhileStatement(WhileStatementSyntax node)
        => StatementTranspilers.TranspileWhileStatement(node, ref _context);

    public override void VisitDoStatement(DoStatementSyntax node)
        => StatementTranspilers.TranspileDoStatement(node, ref _context);

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
        => StatementTranspilers.TranspileSwitchStatement(node, ref _context);

    public override void VisitSwitchSection(SwitchSectionSyntax node)
        => StatementTranspilers.TranspileSwitchSection(node, ref _context);

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        _context.Writer.AppendKeyword("case");
        _context.Writer.AppendSpace();
        Visit(node.Value);
        _context.Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node) => _context.Writer.Append("default:");

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => StatementTranspilers.TranspileBreakStatement(node, ref _context);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => StatementTranspilers.TranspileContinueStatement(node, ref _context);

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => StatementTranspilers.TranspileReturnStatement(node, ref _context);

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        => DeclarationTranspilers.TranspileLocalDeclarationStatement(node, ref _context);

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        => DeclarationTranspilers.TranspileVariableDeclaration(node, ref _context);

    // --- Expressions ---

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (MacroTranspiler.TryTranspileMacro(node, ref _context))
        {
            return;
        }

        Visit(node.Expression);
        _context.Writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => ExpressionTranspilers.TranspileAssignmentExpression(node, ref _context);

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        => ExpressionTranspilers.TranspileBinaryExpression(node, ref _context);

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (_context.LocalVariableOverrides.TryGetValue(node.Identifier.Text, out var overrideValue))
        {
            overrideValue.WriteTo(this);
            return;
        }

        var symbol = _context.SemanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            _context.Writer.AppendConstantField(field);
            return;
        }

        _context.Writer.Append(node.Identifier.Text);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node) =>
        _context.Writer.Append(node.Token.Text);

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileElementAccessExpression(node, ref _context);

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePrefixUnaryExpression(node, ref _context);

    public override void VisitCastExpression(CastExpressionSyntax node)
        => ExpressionTranspilers.TranspileCastExpression(node, ref _context);

    public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        => ExpressionTranspilers.TranspileParenthesizedExpression(node, ref _context);

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePostfixUnaryExpression(node, ref _context);

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileMemberAccessExpression(node, ref _context);

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        => ExpressionTranspilers.TranspileInvocationExpression(node, ref _context);

    public override void VisitArgument(ArgumentSyntax node)
        => ExpressionTranspilers.TranspileArgument(node, ref _context);

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileObjectCreationExpression(node, ref _context);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileImplicitObjectCreationExpression(node, ref _context);
}
