using System.Collections.Concurrent;
using Kassyi.GSharp.Compiler.Transpiler.Rules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Pooled Roslyn syntax visitor that transpiles C# statements and expressions directly into CUDA C++.</summary>
/// <remarks><para>Implements: <b>GEN-15</b></para></remarks>
public class RoslynToCudaVisitor : CSharpSyntaxWalker, IDisposable
{
    // [WHY] Pooled visitor instances eliminate heap allocation overhead during large-scale source generation.
    private static readonly ConcurrentBag<RoslynToCudaVisitor> s_pool = [];

    // Pooled Collections to achieve true zero-allocation during generation
    private readonly HashSet<INamedTypeSymbol> _usedStructs = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<string, OverrideValue> _localVariableOverrides = new();

    internal TranspileContext Context;
    private int _isRented;

    private RoslynToCudaVisitor()
    {
    }

    public static RoslynToCudaVisitor Rent(
        ITranspilerDialect dialect,
        SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>? bclIntrinsicMap = null)
    {
        if (!s_pool.TryTake(out var visitor))
        {
            visitor = new RoslynToCudaVisitor();
        }

        Volatile.Write(ref visitor._isRented, 1);

        visitor._usedStructs.Clear();
        visitor._localVariableOverrides.Clear();

        visitor.Context = new TranspileContext(
            default, // Writer is injected by MethodTranspiler
            visitor._usedStructs,
            visitor._localVariableOverrides,
            semanticModel,
            reportDiagnostic,
            visitor,
            bclIntrinsicMap ?? ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty,
            dialect);

        return visitor;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isRented, 0) != 1)
        {
            return;
        }

        Context = default;
        s_pool.Add(this);
    }

    public override void Visit(SyntaxNode? node)
    {
        if (node == null)
        {
            return;
        }

        var interceptor = Context.Dialect.GetInterceptorVisitor(ref Context);
        var handledByDialect = interceptor.Visit(node);

        if (handledByDialect)
        {
            return;
        }

        base.Visit(node);
    }

    public override void DefaultVisit(SyntaxNode node)
    {
        Context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
            node.Kind().ToString()));
        base.DefaultVisit(node);
    }

    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            Context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.StringNotSupported,
                node.GetLocation()));
        }

        var typeSymbol = Context.SemanticModel.GetTypeInfo(node).Type;
        if (typeSymbol != null)
        {
            Context.AppendType(typeSymbol);
        }
        else
        {
            Context.Writer.Append(node.Keyword.Text);
        }
    }

    public override void VisitParameterList(ParameterListSyntax node)
    {
        using (Context.Writer.Parentheses())
        {
            for (var i = 0; i < node.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    Context.Writer.Append(", ");
                }

                Visit(node.Parameters[i]);
            }
        }
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        if (node.Type != null)
        {
            var typeSymbol = Context.SemanticModel.GetTypeInfo(node.Type).Type;
            if (typeSymbol != null)
            {
                Context.AppendType(typeSymbol);
            }
            else
            {
                Visit(node.Type);
            }
        }
        else
        {
            // Implicitly typed parameter
            Context.Writer.AppendKeyword("auto");
        }

        Context.Writer.Append($" {node.Identifier.Text}");
    }

    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            using (Context.Writer.Scope())
            {
                foreach (var stmt in block.Statements)
                {
                    Visit(stmt);
                    Context.Writer.AppendLine();
                }
            }
        }
        else
        {
            // [WHY] Automatically wraps single-statement bodies (like unbraced if/for branches) in C++ braces to prevent scoping bugs in generated CUDA code.
            using (Context.Writer.Scope())
            {
                Visit(statement);
                if (!statement.IsKind(SyntaxKind.Block))
                {
                    Context.Writer.AppendLine();
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
            Context.Writer.AppendLine();
        }
    }

    public override void VisitIfStatement(IfStatementSyntax node)
        => StatementTranspilers.TranspileIfStatement(node, ref Context);

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Visit(node.Declaration);
        Context.Writer.AppendSemiColonLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        => VisitStatementOrBlock(node.Block);

    public override void VisitForStatement(ForStatementSyntax node)
        => StatementTranspilers.TranspileForStatement(node, ref Context);

    public override void VisitForEachStatement(ForEachStatementSyntax node)
        => StatementTranspilers.TranspileForEachStatement(node, ref Context);

    public override void VisitWhileStatement(WhileStatementSyntax node)
        => StatementTranspilers.TranspileWhileStatement(node, ref Context);

    public override void VisitDoStatement(DoStatementSyntax node)
        => StatementTranspilers.TranspileDoStatement(node, ref Context);

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
        => StatementTranspilers.TranspileSwitchStatement(node, ref Context);

    public override void VisitSwitchSection(SwitchSectionSyntax node)
        => StatementTranspilers.TranspileSwitchSection(node, ref Context);

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        Context.Writer.Append("case ");
        Visit(node.Value);
        Context.Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node) => Context.Writer.Append("default:");

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => StatementTranspilers.TranspileBreakStatement(node, ref Context);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => StatementTranspilers.TranspileContinueStatement(node, ref Context);

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => StatementTranspilers.TranspileReturnStatement(node, ref Context);

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        => DeclarationTranspilers.TranspileLocalDeclarationStatement(node, ref Context);

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        => DeclarationTranspilers.TranspileVariableDeclaration(node, ref Context);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        => StatementTranspilers.TranspileLocalFunctionStatement(node, ref Context);

    // --- Expressions ---

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (MacroTranspiler.TryTranspileMacro(node, ref Context))
        {
            return;
        }

        DeclarationTranspilers.HoistDeclarationExpressions(node, ref Context);
        Visit(node.Expression);
        Context.Writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => AssignmentExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        => BinaryExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        => SwitchExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        => ConditionalExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (Context.LocalVariableOverrides.TryGetValue(node.Identifier.Text, out var overrideValue))
        {
            overrideValue.WriteTo(this);
            return;
        }

        var symbol = Context.SemanticModel.GetSymbolInfo(node).Symbol;
        if (symbol is IFieldSymbol { IsConst: true, HasConstantValue: true } field)
        {
            Context.Writer.AppendConstantField(field);
            return;
        }

        if (symbol is IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true })
        {
            using (Context.Writer.Parentheses())
            {
                Context.Writer.Append($"*{node.Identifier.Text}");
            }

            return;
        }

        Context.Writer.Append(node.Identifier.Text);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            Context.Writer.AppendEmptyScope();
            return;
        }

        Context.Writer.Append(node.Token.Text);
    }

    public override void VisitDefaultExpression(DefaultExpressionSyntax node) => Context.Writer.AppendEmptyScope();

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => ElementAccessExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => PrefixUnaryExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitCastExpression(CastExpressionSyntax node)
        => CastExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        => ParenthesizedExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        => PostfixUnaryExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => MemberAccessExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        => InvocationExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitArgument(ArgumentSyntax node)
        => ArgumentTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        => ObjectCreationExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        => ImplicitObjectCreationExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        => Visit(node.Designation);

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        => Context.Writer.Append(node.Identifier.Text);

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        => LambdaExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        => LambdaExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        => LambdaExpressionTranspilerRule.Instance.Transpile(node, ref Context);

    public override void VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        using (Context.Writer.Braces())
        {
            Context.Writer.AppendSpace();
            for (var i = 0; i < node.Elements.Count; i++)
            {
                Visit(node.Elements[i]);
                if (i >= node.Elements.Count - 1)
                {
                    continue;
                }

                Context.Writer.AppendComma();
                Context.Writer.AppendSpace();
            }

            Context.Writer.AppendSpace();
        }
    }

    public override void VisitExpressionElement(ExpressionElementSyntax node) => Visit(node.Expression);

    public override void VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        using (Context.Writer.Braces())
        {
            Context.Writer.AppendSpace();
            for (var i = 0; i < node.Expressions.Count; i++)
            {
                Visit(node.Expressions[i]);
                if (i >= node.Expressions.Count - 1)
                {
                    continue;
                }

                Context.Writer.AppendComma();
                Context.Writer.AppendSpace();
            }

            Context.Writer.AppendSpace();
        }
    }

    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node)
    {
        if (node.Initializer != null)
        {
            Visit(node.Initializer);
        }
        else
        {
            Context.Writer.AppendEmptyScope();
        }
    }

    public override void VisitImplicitStackAllocArrayCreationExpression(
        ImplicitStackAllocArrayCreationExpressionSyntax node) => Visit(node.Initializer);
}
