using Kassyi.GSharp.Compiler.Transpiler.Rules;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Traverses and transpiles Roslyn C# syntax nodes directly into CUDA C++ source representations using pooled visitor instances.
/// </summary>
/// <remarks>
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
    private readonly OverrideScopeStack _localVariableOverrides = new();
    private readonly Stack<int> _activeSwitchIds = new();

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
    /// <param name="ruleRegistry">An optional registry providing transpilation rules.</param>
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
        visitor._activeSwitchIds.Clear();

        visitor.Context = new TranspileContext(
            default, // Writer is injected by MethodTranspiler
            visitor._usedStructs,
            visitor._localVariableOverrides,
            visitor._activeSwitchIds,
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

        var interceptor = Context.Emitter.Dialect.GetInterceptorVisitor(ref Context);
        var handledByDialect = interceptor.Visit(node);

        if (handledByDialect)
        {
            return;
        }

        if (Context.RuleRegistry.GetRule(node.Kind()) is { } rule)
        {
            rule.Transpile(node, ref Context);
            return;
        }

        base.Visit(node);
    }

    /// <inheritdoc />
    public override void DefaultVisit(SyntaxNode node)
    {
        Context.Reporter.Report(GSharpDiagnostics.UnsupportedSyntax, node, node.Kind().ToString());

        // [WHY] Descending into the children would splice their raw tokens into the output (invalid C++)
        // and re-report every nested node. A single diagnostic plus a placeholder keeps the output inspectable.
        Context.Emitter.Writer.Append("/*unsupported:");
        Context.Emitter.Writer.Append(node.Kind().ToString());
        Context.Emitter.Writer.Append("*/");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only <c>return ref expr;</c> and <c>=&gt; ref expr</c> are supported: the enclosing method is emitted with a C++
    /// reference return type, so the operand is emitted as a plain lvalue. Ref locals and other positions remain unsupported.
    /// </remarks>
    public override void VisitRefExpression(RefExpressionSyntax node)
    {
        if (node.Parent is ReturnStatementSyntax or ArrowExpressionClauseSyntax)
        {
            Visit(node.Expression);
            return;
        }

        DefaultVisit(node);
    }

    /// <inheritdoc />
    public override void VisitPredefinedType(PredefinedTypeSyntax node)
    {
        if (node.Keyword.IsKind(SyntaxKind.StringKeyword))
        {
            Context.Reporter.Report(GSharpDiagnostics.StringNotSupported, node);
        }

        var typeSymbol = Context.Query.SemanticModel.GetTypeSymbol(node);
        if (typeSymbol != null)
        {
            Context.Emitter.Dialect.AppendType(ref Context, typeSymbol);
        }
        else
        {
            Context.Emitter.Writer.Append(node.Keyword.Text);
        }
    }

    public override void VisitEqualsValueClause(EqualsValueClauseSyntax node)
    {
        // Transpiler rules manually extract the underlying Value expression (e.g., in variable declarations)
        // or ignore it (e.g., in parameter default values). Therefore, we intentionally do not traverse it.
    }

    /// <inheritdoc />
    public override void VisitParameterList(ParameterListSyntax node)
    {
        using (Context.Emitter.Writer.Parentheses())
        {
            for (var i = 0; i < node.Parameters.Count; i++)
            {
                if (i > 0)
                {
                    Context.Emitter.Writer.Append(", ");
                }

                Visit(node.Parameters[i]);
            }
        }
    }

    /// <inheritdoc />
    public override void VisitParameter(ParameterSyntax node)
    {
        var paramSymbol = Context.Query.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(node);
        var isReadOnly = paramSymbol?.HasAttribute(KnownAttributes.GpuReadOnly) == true;

        if (node.Type != null)
        {
            var typeSymbol = Context.Query.SemanticModel.GetTypeSymbol(node.Type);
            if (typeSymbol != null)
            {
                var isArray = typeSymbol.TryGetArrayLikeElementType(out var elementType);
                
                if (isReadOnly && !isArray)
                {
                    Context.Emitter.Writer.Append("const ");
                }

                if (isArray)
                {
                    Context.Emitter.Writer.Append("GSharpArray<");
                    if (isReadOnly)
                    {
                        Context.Emitter.Writer.Append("const ");
                    }

                    Context.Emitter.Dialect.AppendType(ref Context, elementType);
                    Context.Emitter.Writer.Append(">");
                }
                else
                {
                    Context.Emitter.Dialect.AppendType(ref Context, typeSymbol);
                }
            }
            else
            {
                if (isReadOnly)
                {
                    Context.Emitter.Writer.Append("const ");
                }

                Visit(node.Type);
            }
        }
        else
        {
            // Implicitly typed parameter
            Context.Emitter.Writer.AppendKeyword("auto");
        }

        Context.Emitter.Writer.Append($" {node.Identifier.Text}");
    }

    /// <summary>
    /// Transpiles a statement or block syntax node, automatically ensuring enclosing C++ braces are emitted for single statements.
    /// </summary>
    /// <param name="statement">The Roslyn statement syntax node to transpile.</param>
    public void VisitStatementOrBlock(StatementSyntax statement)
    {
        // [WHY] Automatically wraps single-statement bodies (like unbraced if/for branches) in C++ braces to prevent scoping bugs in generated CUDA code.
        using (Context.Emitter.Writer.Scope())
        {
            if (statement is BlockSyntax block)
            {
                VisitBlock(block);
            }
            else
            {
                Visit(statement);
                Context.Emitter.Writer.AppendLine();
            }
        }
    }


    // --- Statements ---

    public override void VisitBlock(BlockSyntax node)
    {
        if (Context.Emitter.AutoSyncGridDepth > 0)
        {
            var flatStatements = FlattenStatements(node.Statements, Context.Query.SemanticModel);
            VisitStatementsWithAutoSync(flatStatements);
            return;
        }

        foreach (var statement in node.Statements)
        {
            Visit(statement);
            Context.Emitter.Writer.AppendLine();
        }
    }

    internal void VisitStatementsWithAutoSync(IReadOnlyList<CSharpSyntaxNode> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            Visit(statements[i]);
            Context.Emitter.Writer.AppendLine();
            if (ShouldInsertAutoSync(statements, i))
            {
                Context.Emitter.Writer.AppendLine("cg::this_grid().sync();");
            }
        }
    }

    internal bool ShouldInsertAutoSync(IReadOnlyList<CSharpSyntaxNode> statements, int currentIndex)
    {
        if (Context.Emitter.AutoSyncGridDepth <= 0 || currentIndex >= statements.Count - 1)
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

            return !IsParameterlessSyncGrid(next);
        }

        return false;
    }

    internal bool IsEmptyOrSyncGrid(CSharpSyntaxNode stmt) =>
        stmt is EmptyStatementSyntax || IsParameterlessSyncGrid(stmt);

    internal bool IsParameterlessSyncGrid(CSharpSyntaxNode stmt)
    {
        var invocation = stmt switch
        {
            ExpressionStatementSyntax exprStmt => exprStmt.Expression as InvocationExpressionSyntax,
            InvocationExpressionSyntax inv => inv,
            _ => null
        };

        if (invocation == null)
        {
            return false;
        }

        return Context.Query.SemanticModel.GetSymbol(invocation) is IMethodSymbol
        {
            Name: TypeConstants.GpuMacros.SyncGrid,
            ContainingType.Name: TypeConstants.GpuType,
            Parameters.Length: 0
        } symbol &&
               symbol.ContainingNamespace.IsCoreNamespace();
    }

    internal static IReadOnlyList<CSharpSyntaxNode> FlattenStatements(
        IReadOnlyList<StatementSyntax> statements,
        SemanticModel semanticModel)
    {
        var result = ImmutableArray.CreateBuilder<CSharpSyntaxNode>(statements.Count);
        FlattenInto(statements, semanticModel, result);
        return result.ToImmutable();
    }

    private static void FlattenInto(
        IEnumerable<StatementSyntax> statements,
        SemanticModel semanticModel,
        ImmutableArray<CSharpSyntaxNode>.Builder result)
    {
        foreach (var stmt in statements)
        {
            if (TryGetSyncGridLambdaBody(stmt, semanticModel, out var body))
            {
                switch (body)
                {
                    case BlockSyntax innerBlock:
                        FlattenInto(innerBlock.Statements, semanticModel, result);
                        break;
                    case ExpressionSyntax innerExpr:
                        result.Add(innerExpr);
                        break;
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
        if (stmt is not ExpressionStatementSyntax { Expression: InvocationExpressionSyntax invocation })
        {
            return false;
        }

        if (semanticModel.GetSymbol(invocation) is not IMethodSymbol
            {
                Name: TypeConstants.GpuMacros.SyncGrid
            } symbol ||
            symbol.ContainingType.Name != TypeConstants.GpuType ||
            !symbol.ContainingNamespace.IsCoreNamespace() ||
            symbol.Parameters.Length != 1 ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not LambdaExpressionSyntax lambda)
        {
            return false;
        }

        body = lambda.Body;
        return true;
    }

    public override void VisitIfStatement(IfStatementSyntax node)
        => StatementTranspilers.TranspileIfStatement(node, ref Context);

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Visit(node.Declaration);
        Context.Emitter.Writer.AppendSemiColonLine();
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
        Context.Emitter.Writer.Append("case ");
        Visit(node.Value);
        Context.Emitter.Writer.AppendLine(":");
        var activeId = Context.Emitter.CurrentSwitchId;
        Context.Emitter.Writer.Append($"__gsharp_switch_{activeId}_state_");
        Visit(node.Value);
        Context.Emitter.Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
    {
        var activeId = Context.Emitter.CurrentSwitchId;
        Context.Emitter.Writer.Append($"default:\n__gsharp_switch_{activeId}_state_default:");
    }

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => StatementTranspilers.TranspileBreakStatement(node, ref Context);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => StatementTranspilers.TranspileContinueStatement(node, ref Context);

    public override void VisitGotoStatement(GotoStatementSyntax node)
        => StatementTranspilers.TranspileGotoStatement(node, ref Context);

    public override void VisitLabeledStatement(LabeledStatementSyntax node)
        => StatementTranspilers.TranspileLabeledStatement(node, ref Context);

    public override void VisitEmptyStatement(EmptyStatementSyntax node) => Context.Emitter.Writer.AppendSemiColonLine();

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => StatementTranspilers.TranspileReturnStatement(node, ref Context);

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
        Context.Emitter.Writer.AppendSemiColon();
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (Context.Emitter.TryGetOverride(node.Identifier.Text, out var overrideValue))
        {
            overrideValue.WriteTo(this);
            return;
        }

        var symbol = Context.Query.SemanticModel.GetSymbol(node);
        switch (symbol)
        {
            case IFieldSymbol { IsConst: true, HasConstantValue: true } field:

                Context.Emitter.Writer.AppendConstantField(field);

                return;
            case IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true }:
                {
                    using (Context.Emitter.Writer.Parentheses())
                    {
                        Context.Emitter.Writer.Append($"*{node.Identifier.Text}");
                    }

                    return;
                }
            default:
                Context.Emitter.Writer.Append(node.Identifier.Text);
                break;
        }
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            Context.Emitter.Writer.AppendEmptyScope();
            return;
        }

        if (node.IsKind(SyntaxKind.NullLiteralExpression))
        {
            Context.Emitter.Writer.Append("nullptr");
            return;
        }

        // C# long/ulong are always 64-bit, but C++ 'L'/'UL' are 32-bit under LLP64 (MSVC, Windows nvcc).
        if (node.IsKind(SyntaxKind.NumericLiteralExpression) && node.Token.Value is long or ulong)
        {
            AppendInt64Literal(node.Token.Text, node.Token.Value is ulong);
            return;
        }

        Context.Emitter.Writer.Append(node.Token.Text);
    }

    private void AppendInt64Literal(string text, bool isUnsigned)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is 'u' or 'U' or 'l' or 'L')
        {
            end--;
        }

        for (var i = 0; i < end; i++)
        {
            Context.Emitter.Writer.Append(text[i]);
        }

        Context.Emitter.Writer.Append(isUnsigned ? "ull" : "ll");
    }

    public override void VisitDefaultExpression(DefaultExpressionSyntax node) =>
        Context.Emitter.Writer.AppendEmptyScope();

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        => Visit(node.Designation);

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        => Context.Emitter.Writer.Append(node.Identifier.Text);

    public override void VisitCollectionExpression(CollectionExpressionSyntax node) =>
        VisitBracedSeparatedList(node.Elements);

    public override void VisitExpressionElement(ExpressionElementSyntax node) => Visit(node.Expression);

    public override void VisitInitializerExpression(InitializerExpressionSyntax node) =>
        VisitBracedSeparatedList(node.Expressions);

    private void VisitBracedSeparatedList<TNode>(SeparatedSyntaxList<TNode> elements) where TNode : SyntaxNode
    {
        using (Context.Emitter.Writer.Braces())
        {
            Context.Emitter.Writer.AppendSpace();
            for (var i = 0; i < elements.Count; i++)
            {
                Visit(elements[i]);
                if (i >= elements.Count - 1)
                {
                    continue;
                }

                Context.Emitter.Writer.AppendComma();
                Context.Emitter.Writer.AppendSpace();
            }

            Context.Emitter.Writer.AppendSpace();
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
            Context.Emitter.Writer.AppendEmptyScope();
        }
    }

    public override void VisitImplicitStackAllocArrayCreationExpression(
        ImplicitStackAllocArrayCreationExpressionSyntax node) => Visit(node.Initializer);
}
