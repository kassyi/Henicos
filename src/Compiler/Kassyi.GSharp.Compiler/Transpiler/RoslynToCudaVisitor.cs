using Kassyi.GSharp.Compiler.Transpiler.Rules.ExpressionRules;
using Kassyi.GSharp.Compiler.Transpiler.Rules.MacroRules;

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

        visitor._usedStructs.Clear();
        visitor._localVariableOverrides.Clear();
        visitor._activeSwitchIds.Clear();

        visitor.Context = new TranspileContext(
            default, // Writer is injected by MethodTranspiler
            visitor._usedStructs,
            visitor._localVariableOverrides,
            visitor._activeSwitchIds,
            semanticModel,
            reportFailure,
            visitor,
            bclIntrinsicMap ?? ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty,
            dialect);

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

        var previousNode = Context.Emitter.CurrentNode;
        Context.Emitter.CurrentNode = node;

        try
        {
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
        foreach (var statement in node.Statements)
        {
            // [WHY] A nested block is emitted without braces, since lowering wraps statements of one scope in blocks; a
            // block with a using declaration or a synthesized iterator scope keeps them.
            if (statement is BlockSyntax nested &&
                (UsingTranspiler.DeclaresUsing(nested) ||
                 nested.HasAnnotations(LoweringAnnotations.ScopedBlockAnnotationKind)))
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
        SwitchStateLabel.Append(node.Value, ref Context);
        Context.Emitter.Writer.Append(":");
    }

    public override void VisitDefaultSwitchLabel(DefaultSwitchLabelSyntax node)
    {
        var activeId = Context.Emitter.CurrentSwitchId;
        Context.Emitter.Writer.Append($"default:\n{GSharpInternalNames.Lowering.SwitchStatePrefix}{activeId}_state_default:");
    }

    public override void VisitBreakStatement(BreakStatementSyntax node)
        => StatementTranspilers.TranspileBreakStatement(node, ref Context);

    public override void VisitContinueStatement(ContinueStatementSyntax node)
        => StatementTranspilers.TranspileContinueStatement(node, ref Context);

    public override void VisitGotoStatement(GotoStatementSyntax node)
        => StatementTranspilers.TranspileGotoStatement(node, ref Context);

    public override void VisitLabeledStatement(LabeledStatementSyntax node)
        => StatementTranspilers.TranspileLabeledStatement(node, ref Context);

    public override void VisitEmptyStatement(EmptyStatementSyntax node) => Context.Emitter.Writer.AppendLine(";");

    public override void VisitReturnStatement(ReturnStatementSyntax node)
        => StatementTranspilers.TranspileReturnStatement(node, ref Context);

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        => StatementTranspilers.TranspileLocalFunctionStatement(node, ref Context);

    // --- Expressions ---

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (Context.RuleRegistry.TryTranspileMacro(node, ref Context))
        {
            return;
        }

        StatementPrelude.Emit(node, ref Context);
        Visit(node.Expression);
        Context.Emitter.Writer.Append(';');
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (Context.Emitter.TryGetOverride(node.Identifier.Text, out var overrideValue))
        {
            overrideValue.WriteTo(this);
            return;
        }

        var symbol = Context.Query.SemanticModel.GetSymbol(node);

        // A Core intrinsic named without its type (using static Kassyi.GSharp.Core.Gpu) maps as Gpu.<name> does (#565)
        if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol &&
            symbol is { IsStatic: true, ContainingType: { } owner } &&
            owner.ContainingNamespace.IsCoreNamespace() &&
            MemberAccessExpressionTranspilerRule.TryTranspileIntrinsic(symbol, ref Context))
        {
            return;
        }

        switch (symbol)
        {
            case IFieldSymbol { IsConst: true, HasConstantValue: true } field:

                ConstantAtUse.Append(node, field.Type, field.ConstantValue, ref Context);

                return;
            case IParameterSymbol { RefKind: RefKind.Ref or RefKind.Out } or ILocalSymbol { IsRef: true }:
            case IParameterSymbol { RefKind: RefKind.In or RefKind.RefReadOnlyParameter, ContainingSymbol: IMethodSymbol paramMethod }
                when !paramMethod.HasAttribute(KnownAttributes.GpuKernel):
                {
                    using (Context.Emitter.Writer.Parentheses())
                    {
                        Context.Emitter.Writer.Append('*');
                        CppNameResolver.AppendSymbolName(symbol, ref Context);
                    }

                    return;
                }
            default:
                // [WHY] Lowering rewrites hoisted locals into references that carry the declarator's original span.
                symbol ??= Context.Query.SemanticModel.GetDeclaredSymbol<ISymbol>(node);

                if (symbol != null &&
                    GpuCodeConstraints.IsHostStaticMemberReference(node, symbol, Context.Query.SemanticModel, Context.Emitter.Dialect))
                {
                    Context.Reporter.Fail(
                        GSharpDiagnostics.StaticMemberAccessNotSupported,
                        node,
                        symbol.ContainingType?.Name ?? string.Empty,
                        symbol.Name);
                }

                if (symbol is IMethodSymbol method)
                {
                    DynamicSharedMemoryEmitter.FailIfReferencedWithoutCall(node, method, ref Context);
                }

                // A member function reads a captured primary constructor parameter from its hidden field (#495)
                if (symbol is IParameterSymbol parameter &&
                    CppNameResolver.IsCapturedParameterReference(parameter, ref Context))
                {
                    Context.Emitter.Writer.AppendCppName(CppName.Member(parameter.Name));
                    break;
                }

                // In a lowered struct constructor function, unqualified instance members belong to __gsharp_self.
                if (Context.Emitter.StructConstructorType is { } ctorType &&
                    symbol is IFieldSymbol { IsStatic: false, IsConst: false } or IPropertySymbol { IsStatic: false } &&
                    symbol.ContainingType != null &&
                    symbol.ContainingType.OriginalDefinition.IsEqualTo(ctorType.OriginalDefinition) &&
                    node.Parent is not AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax })
                {
                    Context.Emitter.Writer.Append(GSharpInternalNames.Lowering.ConstructorSelf).Append('.');
                }

                Context.Emitter.Writer.AppendCppName(
                    CppNameResolver.Resolve(
                        symbol,
                        node.Identifier.ValueText,
                        Context.Emitter.Dialect,
                        LoweringIdentity.GetRegion(node)));
                ComputedPropertyGetterCall.AppendCallIfGetter(node, symbol, ref Context);
                break;
        }
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.DefaultLiteralExpression))
        {
            Context.Emitter.Writer.Append("{}");
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

        // C# accepts "2f" and "2d", but C++ treats "2" as an integer literal (so "f" is an unknown suffix) and has no "d" suffix.
        if (node.IsKind(SyntaxKind.NumericLiteralExpression) && node.Token.Value is float or double)
        {
            AppendFloatingPointLiteral(node.Token.Text, node.Token.Value is float);
            return;
        }

        // A C# char is a UTF-16 code unit; the C++ character literal is a char in the source's encoding ('é' is two bytes).
        if (node.IsKind(SyntaxKind.CharacterLiteralExpression) && node.Token.Value is char character)
        {
            Context.Emitter.Writer.Append(ConstexprLiteral.FormatCppChar(character));
            return;
        }

        // A string with a non-ASCII character (a source file name in a CPU error message, a printf format) is written with
        // octal escapes, so its UTF-8 bytes do not depend on the character sets of the C++ compiler; any other string is
        // written as it is, its C# escapes being C++ escapes as well
        if (node.IsKind(SyntaxKind.StringLiteralExpression) && HasNonAscii(node.Token.ValueText))
        {
            CppStringLiteral.Append(Context.Emitter.Writer, node.Token.ValueText);
            return;
        }

        // C++ rejects '_' inside numbers (and "0x'FF"), so digit separators are dropped.
        if (node.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            AppendWithoutSeparators(node.Token.Text, node.Token.Text.Length);
            return;
        }

        Context.Emitter.Writer.Append(node.Token.Text);
    }

    private static bool HasNonAscii(string value)
    {
        foreach (var c in value)
        {
            if (c > 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    private void AppendWithoutSeparators(string text, int end)
    {
        for (var i = 0; i < end; i++)
        {
            if (text[i] != '_')
            {
                Context.Emitter.Writer.Append(text[i]);
            }
        }
    }

    private void AppendFloatingPointLiteral(string text, bool isSingle)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is 'f' or 'F' or 'd' or 'D')
        {
            end--;
        }

        var hasFraction = false;
        for (var i = 0; i < end; i++)
        {
            hasFraction |= text[i] is '.' or 'e' or 'E';
        }

        AppendWithoutSeparators(text, end);

        if (!hasFraction)
        {
            Context.Emitter.Writer.Append(".0");
        }

        if (isSingle)
        {
            Context.Emitter.Writer.Append('f');
        }
    }

    private void AppendInt64Literal(string text, bool isUnsigned)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] is 'u' or 'U' or 'l' or 'L')
        {
            end--;
        }

        AppendWithoutSeparators(text, end);

        Context.Emitter.Writer.Append(isUnsigned ? "ull" : "ll");
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
                Visit(elements[i]);
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
