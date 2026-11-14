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
    /// <param name="reportFailure">An optional callback that receives the constructs that could not be emitted (<see cref="TranspileFailure"/>).</param>
    /// <param name="bclIntrinsicMap">An optional lookup dictionary mapping BCL method symbols to target intrinsics.</param>
    /// <returns>A pooled and initialized <see cref="RoslynToCudaVisitor"/> instance ready for traversal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dialect"/> or <paramref name="semanticModel"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>The returned instance must be disposed using <see cref="Dispose"/> to return it to the pool.</para>
    /// </remarks>
    public static RoslynToCudaVisitor Rent(
        ITranspilerDialect dialect,
        SemanticModel semanticModel, Action<TranspileFailure>? reportFailure = null,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>? bclIntrinsicMap = null)
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
        visitor.Reset(dialect, semanticModel, reportFailure, bclIntrinsicMap);
        return visitor;
    }

    // Clears the pooled collections and starts a new context over them
    private void Reset(
        ITranspilerDialect dialect,
        SemanticModel semanticModel,
        Action<TranspileFailure>? reportFailure,
        ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>? bclIntrinsicMap)
    {
        _usedStructs.Clear();
        _localVariableOverrides.Clear();
        _activeSwitchIds.Clear();

        Context = new TranspileContext(
            default, // Writer is injected by MethodTranspiler
            _usedStructs,
            _localVariableOverrides,
            _activeSwitchIds,
            semanticModel,
            reportFailure,
            this,
            bclIntrinsicMap ?? ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty,
            dialect);
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

        var previousNode = Context.Emitter.CurrentNode;
        Context.Emitter.CurrentNode = node;

        try
        {
            if (node is ExpressionSyntax expression && TryIntercept(expression))
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
        finally
        {
            Context.Emitter.CurrentNode = previousNode;
        }
    }

    // The special cases that run before the rule of the expression's kind
    // (TranspilerRuleRegistry.ExpressionInterceptors)
    private bool TryIntercept(ExpressionSyntax expression)
    {
        foreach (var interceptor in Context.RuleRegistry.ExpressionInterceptors)
        {
            if (interceptor.TryAppend(expression, ref Context))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The boundary of the syntax G# emits: a kind that no rule and no override handles, including a kind that a newer C#
    /// adds, becomes a GSH0003 failure, which the analyzer reports (no list of rejected kinds to keep up to date).
    /// </remarks>
    public override void DefaultVisit(SyntaxNode node)
    {
        Context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, node.Kind().ToString());
        AppendUnsupportedPlaceholder(node);
    }

    /// <summary>Writes a placeholder for a construct the transpiler cannot emit.</summary>
    /// <remarks>
    /// [WHY] Descending into the children would splice their raw tokens into the output (invalid C++)
    /// and re-report every nested node. A single diagnostic plus a placeholder keeps the output inspectable.
    /// </remarks>
    internal void AppendUnsupportedPlaceholder(SyntaxNode node)
    {
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
            Context.Reporter.Fail(GSharpDiagnostics.StringNotSupported, node);
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
        if (node.Type != null)
        {
            var typeSymbol = Context.Query.SemanticModel.GetTypeSymbol(node.Type);
            if (typeSymbol != null)
            {
                var paramSymbol = Context.Query.SemanticModel.GetDeclaredSymbol<IParameterSymbol>(node);
                MethodSignatureTranspiler.WriteParameterDeclaration(node, typeSymbol, paramSymbol, ref Context);
            }
            else
            {
                Visit(node.Type);
            }
        }
        else
        {
            // Implicitly typed parameter
            Context.Emitter.Writer.Append("auto");
        }

        Context.Emitter.Writer.Append(' ');
        CppNameResolver.AppendDeclaredName(node, node.Identifier, ref Context);
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
        // A block written without braces is in the C++ scope of the list around it, which declares its locals
        if (!CppBlockScopes.IsWrittenWithoutBraces(node))
        {
            JumpedOverDeclarations.WriteDeclarations(node.Statements, ref Context);
        }

        VisitStatements(node.Statements, 0);
    }

    /// <summary>
    /// Transpiles the statements of a block from <paramref name="start"/> on. On the CPU a <c>using</c> declaration
    /// writes the statements after it itself, as its body (<see cref="UsingCpuEmitter"/>).
    /// </summary>
    internal void VisitStatements(SyntaxList<StatementSyntax> statements, int start)
    {
        // The local functions that a statement uses before they are declared are written in front of it
        var localFunctions = LocalFunctionOrder.Create(statements, start, Context.Query.SemanticModel);
        for (var i = start; i < statements.Count; i++)
        {
            var statement = statements[i];
            if (localFunctions != null && localFunctions.TryWriteStatement(i, ref Context))
            {
                continue;
            }

            if (!Context.Emitter.Dialect.IsCuda &&
                UsingCpuEmitter.TryGetUsingDeclaration(statement, out var declaration))
            {
                // The labels of a labeled using declaration come first: its body is still the rest of the list
                for (var labeled = statement as LabeledStatementSyntax; labeled != null;
                     labeled = labeled.Statement as LabeledStatementSyntax)
                {
                    JumpStatementTranspilers.WriteLabel(labeled, ref Context);
                }

                UsingCpuEmitter.TranspileUsingDeclaration(declaration, statements, i, ref Context);
                Context.Emitter.Writer.AppendLine();
                return;
            }

            // [WHY] A nested block is emitted without braces, since lowering wraps statements of one scope in blocks; a
            // block with a using declaration or a synthesized iterator scope keeps them (CppBlockScopes).
            if (statement is BlockSyntax nested && !CppBlockScopes.IsWrittenWithoutBraces(nested))
            {
                VisitStatementOrBlock(nested);
            }
            else
            {
                Visit(statement);
            }

            Context.Emitter.Writer.AppendLine();
        }
    }

    public override void VisitIfStatement(IfStatementSyntax node)
        => StatementTranspilers.TranspileIfStatement(node, ref Context);

    public override void VisitUsingStatement(UsingStatementSyntax node)
        => UsingTranspiler.TranspileUsingStatement(node, ref Context);

    public override void VisitFixedStatement(FixedStatementSyntax node)
    {
        Visit(node.Declaration);
        Context.Emitter.Writer.AppendLine(";");
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        => VisitStatementOrBlock(node.Block);

    public override void VisitForStatement(ForStatementSyntax node)
        => LoopStatementTranspilers.TranspileForStatement(node, ref Context);

    public override void VisitForEachStatement(ForEachStatementSyntax node)
        => LoopStatementTranspilers.TranspileForEachStatement(node, ref Context);

    public override void VisitWhileStatement(WhileStatementSyntax node)
        => LoopStatementTranspilers.TranspileWhileStatement(node, ref Context);

    public override void VisitDoStatement(DoStatementSyntax node)
        => LoopStatementTranspilers.TranspileDoStatement(node, ref Context);

    public override void VisitSwitchStatement(SwitchStatementSyntax node)
        => SwitchStatementTranspilers.TranspileSwitchStatement(node, ref Context);

    public override void VisitSwitchSection(SwitchSectionSyntax node)
        => SwitchStatementTranspilers.TranspileSwitchSection(node, ref Context);

    public override void VisitCaseSwitchLabel(CaseSwitchLabelSyntax node)
    {
        Context.Emitter.Writer.Append("case ");
        Visit(node.Value);
        Context.Emitter.Writer.AppendLine(":");
        SwitchStateLabel.Append(node.Value, ref Context);
        Context.Emitter.Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
    {
        var activeId = Context.Emitter.CurrentSwitchId;
        Context.Emitter.Writer.Append($"default:\n{GSharpInternalNames.Lowering.SwitchStatePrefix}{activeId}_state_default:");
    }

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => JumpStatementTranspilers.TranspileBreakStatement(node, ref Context);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => JumpStatementTranspilers.TranspileContinueStatement(node, ref Context);

    public override void VisitGotoStatement(GotoStatementSyntax node)
        => JumpStatementTranspilers.TranspileGotoStatement(node, ref Context);

    public override void VisitLabeledStatement(LabeledStatementSyntax node)
        => JumpStatementTranspilers.TranspileLabeledStatement(node, ref Context);

    public override void VisitEmptyStatement(EmptyStatementSyntax node) => Context.Emitter.Writer.AppendLine(";");

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => ReturnStatementTranspilers.TranspileReturnStatement(node, ref Context);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        => StatementTranspilers.TranspileLocalFunctionStatement(node, ref Context);

    // --- Expressions ---

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (Context.RuleRegistry.TryTranspileMacro(node, ref Context))
        {
            return;
        }

        // C# removes the call of a partial method without an implementing declaration, or of a [Conditional] method
        // whose symbols are not defined, arguments included
        if (RemovedCalls.IsRemovedCall(node.Expression, Context.Query.SemanticModel))
        {
            Context.Emitter.Writer.AppendLine(RemovedCallEraser.IsEmbedded(node) ? "{}" : ";");
            return;
        }

        StatementPrelude.Emit(node, ref Context);
        Visit(node.Expression);
        Context.Emitter.Writer.Append(';');
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
        => IdentifierNameTranspiler.Transpile(node, ref Context);

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!LiteralTranspiler.TryAppendArrayNull(node, ref Context))
        {
            LiteralTranspiler.Transpile(node, Context.Emitter.Writer);
        }
    }

    public override void VisitDefaultExpression(DefaultExpressionSyntax node) =>
        Context.Emitter.Writer.Append("{}");

    public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        => Visit(node.Designation);

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        => CppNameResolver.AppendDeclaredName(node, node.Identifier, ref Context);

    public override void VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        // A collection expression converted to a span or an array outside a stack array declaration fills its storage
        if (!StackAllocEmitter.TryEmit(node, ref Context))
        {
            VisitBracedSeparatedList(node.Elements);
        }
    }

    public override void VisitExpressionElement(ExpressionElementSyntax node) => Visit(node.Expression);

    public override void VisitInitializerExpression(InitializerExpressionSyntax node) =>
        VisitBracedSeparatedList(node.Expressions);

    private void VisitBracedSeparatedList<TNode>(SeparatedSyntaxList<TNode> elements) where TNode : SyntaxNode
    {
        using (Context.Emitter.Writer.Braces())
        {
            Context.Emitter.Writer.Append(' ');
            for (var i = 0; i < elements.Count; i++)
            {
                // An element that C# converts to the element type is written converted: a braced list rejects a
                // narrowing conversion (#897)
                switch (elements[i])
                {
                    case ExpressionElementSyntax element:
                        ArithmeticConversions.AppendElement(element.Expression, ref Context);
                        break;
                    case ExpressionSyntax expression:
                        ArithmeticConversions.AppendElement(expression, ref Context);
                        break;
                    default:
                        Visit(elements[i]);
                        break;
                }

                if (i >= elements.Count - 1)
                {
                    continue;
                }

                Context.Emitter.Writer.Append(", ");
            }

            Context.Emitter.Writer.Append(' ');
        }
    }

    // A declaration writes its array creation as a stack array (VariableDeclarationTranspilerRule); anywhere else the
    // array would live on the heap, which device code does not have (GSH0001), or has more than one dimension (GSH0003)
    public override void VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        if (GpuCodeConstraints.GetStackArrayShapeProblem(node, null, Context.Query.SemanticModel) is
            GpuCodeConstraints.MultidimensionalArray)
        {
            Context.Reporter.Fail(GSharpDiagnostics.UnsupportedSyntax, node, GpuCodeConstraints.MultidimensionalArray);
        }
        else
        {
            Context.Reporter.Fail(GSharpDiagnostics.ObjectCreationNotSupported, node);
        }

        AppendUnsupportedPlaceholder(node);
    }

    public override void VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        Context.Reporter.Fail(GSharpDiagnostics.ObjectCreationNotSupported, node);
        AppendUnsupportedPlaceholder(node);
    }

    // A stackalloc that is not the initializer of a stack array declaration (which writes T name[N] itself) fills the
    // storage that the statement prelude declared (StackAllocEmitter)
    public override void VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node) =>
        StackAllocEmitter.TryEmit(node, ref Context);

    public override void VisitImplicitStackAllocArrayCreationExpression(
        ImplicitStackAllocArrayCreationExpressionSyntax node) =>
        StackAllocEmitter.TryEmit(node, ref Context);
}
