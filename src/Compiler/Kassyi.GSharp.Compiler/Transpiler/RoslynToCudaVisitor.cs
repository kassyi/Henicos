using System.Collections.Concurrent;
using Kassyi.GSharp.Compiler.Transpiler.Rules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Traverses and transpiles Roslyn C# syntax nodes directly into CUDA C++ source representations using pooled visitor instances.
/// </summary>
/// <remarks>
/// <para>Implements: <b>GEN-15</b></para>
/// <para><b>Performance:</b> Instances are pooled via <see cref="Rent"/> and returned via <see cref="Dispose"/> to eliminate heap allocations during large-scale compilation passes.</para>
/// <para><b>Thread Safety:</b> Individual visitor instances are mutable and not thread-safe. However, <see cref="Rent"/> and <see cref="Dispose"/> are safe for multi-threaded invocation.</para>
/// <para><b>Execution Target:</b> Host-only (Roslyn compiler environment). Cannot be executed within GPU kernels.</para>
/// </remarks>
public class RoslynToCudaVisitor : CSharpSyntaxWalker, IDisposable
{
    // [WHY] Pooled visitor instances eliminate heap allocation overhead during large-scale source generation.
    private static readonly ConcurrentBag<RoslynToCudaVisitor> s_pool = [];
    private static readonly Lazy<ITranspilerRuleRegistry> s_defaultRegistry = new(TranspilerRuleRegistry.CreateDefault);

    // [WHY] Reusable collections pooled per visitor instance to ensure zero-allocation during generation.
    private readonly HashSet<INamedTypeSymbol> _usedStructs = new(SymbolEqualityComparer.Default);
    private readonly List<(string Name, OverrideValue Value)> _localVariableOverrides = [];

    internal TranspileContext Context;
    private int _isRented;

    private RoslynToCudaVisitor()
    {
    }

    /// <summary>
    /// Rents an initialized <see cref="RoslynToCudaVisitor"/> instance from the internal object pool.
    /// </summary>
    /// <param name="dialect">The target language and hardware dialect controlling code emission.</param>
    /// <param name="semanticModel">The Roslyn semantic model bound to the active compilation unit.</param>
    /// <param name="reportDiagnostic">An optional callback delegate to report compiler warnings or errors.</param>
    /// <param name="bclIntrinsicMap">An optional lookup dictionary mapping BCL method symbols to target intrinsics.</param>
    /// <returns>A pooled and initialized <see cref="RoslynToCudaVisitor"/> instance ready for traversal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dialect"/> or <paramref name="semanticModel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>The returned instance must be disposed using <see cref="Dispose"/> to return it to the pool.</para>
    /// </remarks>
    public static RoslynToCudaVisitor Rent(
        ITranspilerDialect dialect,
        SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>? bclIntrinsicMap = null,
        ITranspilerRuleRegistry? ruleRegistry = null)
    {
        if (dialect is null)
        {
            throw new ArgumentNullException(nameof(dialect));
        }

        if (semanticModel is null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

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
            dialect,
            ruleRegistry ?? s_defaultRegistry.Value);

        return visitor;
    }

    /// <summary>
    /// Releases the active transpilation context and returns this visitor instance to the object pool.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isRented, 0) != 1)
        {
            return;
        }

        Context = default;
        s_pool.Add(this);
    }


    /// <inheritdoc />
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

    /// <inheritdoc />
    public override void DefaultVisit(SyntaxNode node)
    {
        Context.ReportDiagnostic?.Invoke(Diagnostic.Create(GSharpDiagnostics.UnsupportedSyntax, node.GetLocation(),
            node.Kind().ToString()));
        base.DefaultVisit(node);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <summary>
    /// Transpiles a statement or block syntax node, automatically ensuring enclosing C++ braces are emitted for single statements.
    /// </summary>
    /// <param name="statement">The Roslyn statement syntax node to transpile.</param>
    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        if (statement is BlockSyntax block)
        {
            using (Context.Writer.Scope())
            {
                var statements = Context.AutoSyncGridDepth > 0
                    ? FlattenStatements(block.Statements, Context.SemanticModel)
                    : block.Statements;
                for (var i = 0; i < statements.Count; i++)
                {
                    Visit(statements[i]);
                    Context.Writer.AppendLine();
                    if (ShouldInsertAutoSync(statements, i))
                    {
                        Context.Writer.AppendLine("cg::this_grid().sync();");
                    }
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
        var statements = Context.AutoSyncGridDepth > 0
            ? FlattenStatements(node.Statements, Context.SemanticModel)
            : node.Statements;
        for (var i = 0; i < statements.Count; i++)
        {
            Visit(statements[i]);
            Context.Writer.AppendLine();
            if (ShouldInsertAutoSync(statements, i))
            {
                Context.Writer.AppendLine("cg::this_grid().sync();");
            }
        }
    }

    internal bool ShouldInsertAutoSync(IReadOnlyList<StatementSyntax> statements, int currentIndex)
    {
        if (Context.AutoSyncGridDepth <= 0 || currentIndex >= statements.Count - 1)
        {
            return false;
        }

        var current = statements[currentIndex];
        if (IsEmptyOrSyncGrid(current))
        {
            return false;
        }

        for (var nextIdx = currentIndex + 1; nextIdx < statements.Count; nextIdx++)
        {
            var next = statements[nextIdx];
            if (next is EmptyStatementSyntax)
            {
                continue;
            }

            if (IsParameterlessSyncGrid(next))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    internal bool IsEmptyOrSyncGrid(StatementSyntax stmt) =>
        stmt is EmptyStatementSyntax || IsParameterlessSyncGrid(stmt);

    internal bool IsParameterlessSyncGrid(StatementSyntax stmt)
    {
        if (stmt is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
        {
            if (Context.SemanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol &&
                symbol.Name == TypeConstants.GpuMacros.SyncGrid &&
                symbol.ContainingType.Name == TypeConstants.GpuType &&
                symbol.ContainingNamespace.IsCoreNamespace() &&
                symbol.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    internal static IReadOnlyList<StatementSyntax> FlattenStatements(
        IReadOnlyList<StatementSyntax> statements,
        SemanticModel semanticModel)
    {
        var result = new List<StatementSyntax>(statements.Count);
        FlattenInto(statements, semanticModel, result);
        return result;
    }

    private static void FlattenInto(
        IEnumerable<StatementSyntax> statements,
        SemanticModel semanticModel,
        List<StatementSyntax> result)
    {
        foreach (var stmt in statements)
        {
            if (TryGetSyncGridLambdaBody(stmt, semanticModel, out var body))
            {
                if (body is BlockSyntax innerBlock)
                {
                    FlattenInto(innerBlock.Statements, semanticModel, result);
                }
                else if (body is ExpressionSyntax innerExpr)
                {
                    result.Add(SyntaxFactory.ExpressionStatement(innerExpr));
                }
            }
            else
            {
                result.Add(stmt);
            }
        }
    }

    internal static bool TryGetSyncGridLambdaBody(
        StatementSyntax stmt,
        SemanticModel semanticModel,
        out CSharpSyntaxNode? body)
    {
        body = null;
        if (stmt is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
        {
            if (semanticModel.GetSymbolInfo(invocation).Symbol is IMethodSymbol symbol &&
                symbol.Name == TypeConstants.GpuMacros.SyncGrid &&
                symbol.ContainingType.Name == TypeConstants.GpuType &&
                symbol.ContainingNamespace.IsCoreNamespace() &&
                symbol.Parameters.Length == 1 &&
                invocation.ArgumentList.Arguments.Count == 1 &&
                invocation.ArgumentList.Arguments[0].Expression is LambdaExpressionSyntax lambda)
            {
                body = lambda.Body;
                return true;
            }
        }

        return false;
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
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        => StatementTranspilers.TranspileLocalFunctionStatement(node, ref Context);

    // --- Expressions ---

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (MacroTranspiler.TryTranspileMacro(node, ref Context))
        {
            return;
        }

        DeclarationHoistingHelper.HoistDeclarationExpressions(node, ref Context);
        Visit(node.Expression);
        Context.Writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitSwitchExpression(SwitchExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (Context.TryGetOverride(node.Identifier.Text, out var overrideValue))
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

        if (node.IsKind(SyntaxKind.NullLiteralExpression))
        {
            Context.Writer.Append("nullptr");
            return;
        }

        Context.Writer.Append(node.Token.Text);
    }

    public override void VisitDefaultExpression(DefaultExpressionSyntax node) => Context.Writer.AppendEmptyScope();

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitCastExpression(CastExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitArgument(ArgumentSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        => Visit(node.Designation);

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        => Context.Writer.Append(node.Identifier.Text);

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        => Context.RuleRegistry.GetRule(node.Kind())?.Transpile(node, ref Context);

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
