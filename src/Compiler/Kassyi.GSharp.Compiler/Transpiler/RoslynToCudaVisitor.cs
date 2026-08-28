using System.Collections.Concurrent;

namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Pooled Roslyn syntax visitor that transpiles C# statements and expressions directly into CUDA C++.</summary>
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

    public static RoslynToCudaVisitor Rent(SemanticModel semanticModel, Action<Diagnostic>? reportDiagnostic = null,
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
            bclIntrinsicMap ?? ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo>.Empty);

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

        base.VisitPredefinedType(node);
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
        Context.Writer.AppendSemiColon();
        Context.Writer.AppendLine();
        VisitStatementOrBlock(node.Statement);
    }

    public override void VisitUnsafeStatement(UnsafeStatementSyntax node)
        => VisitStatementOrBlock(node.Block);

    public override void VisitForStatement(ForStatementSyntax node)
        => StatementTranspilers.TranspileForStatement(node, ref Context);

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
        Context.Writer.AppendKeyword("case");
        Context.Writer.AppendSpace();
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
    {
        var methodSymbol = Context.SemanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
        var isRecursive = methodSymbol != null && CheckRecursionWalker.IsRecursive(node, methodSymbol, Context.SemanticModel);

        var name = node.Identifier.Text;

        if (isRecursive)
        {
            Context.Writer.Append($"struct __rec_{name}");
            using (Context.Writer.Scope())
            {
                Context.Writer.Append("__device__ static ");
                var returnTypeSymbol = methodSymbol?.ReturnType;
                if (returnTypeSymbol != null && returnTypeSymbol.SpecialType != SpecialType.System_Void)
                {
                    Context.Writer.Append(CudaTypeMapper.MapType(returnTypeSymbol));
                }
                else
                {
                    Context.Writer.AppendKeyword("void");
                }
                Context.Writer.AppendSpace();
                Context.Writer.Append("call");
                using (Context.Writer.Parentheses())
                {
                    WriteLambdaParameters(node.ParameterList.Parameters, ref Context);
                }
                Context.Writer.AppendSpace();

                Context.LocalVariableOverrides[name] = new OverrideValue($"__rec_{name}::call");
                try
                {
                    if (node.Body != null)
                    {
                        VisitStatementOrBlock(node.Body);
                    }
                    else if (node.ExpressionBody != null)
                    {
                        using (Context.Writer.Scope())
                        {
                            Context.Writer.AppendKeyword("return");
                            Context.Writer.AppendSpace();
                            Visit(node.ExpressionBody.Expression);
                            Context.Writer.AppendSemiColon();
                        }
                    }
                }
                finally
                {
                    Context.LocalVariableOverrides.Remove(name);
                }
            }
            Context.Writer.AppendSemiColon();
            Context.Writer.AppendLine();

            Context.Writer.AppendKeyword("auto");
            Context.Writer.AppendSpace();
            Context.Writer.Append(name);
            Context.Writer.AppendOperator("=");
            Context.Writer.Append("[=] __device__ ");
            using (Context.Writer.Parentheses())
            {
                WriteLambdaParameters(node.ParameterList.Parameters, ref Context);
            }
            using (Context.Writer.Scope())
            {
                var returnTypeSymbol = methodSymbol?.ReturnType;
                if (returnTypeSymbol != null && returnTypeSymbol.SpecialType != SpecialType.System_Void)
                {
                    Context.Writer.AppendKeyword("return");
                    Context.Writer.AppendSpace();
                }
                Context.Writer.Append($"__rec_{name}::call");
                using (Context.Writer.Parentheses())
                {
                    for (var i = 0; i < node.ParameterList.Parameters.Count; i++)
                    {
                        Context.Writer.Append(node.ParameterList.Parameters[i].Identifier.Text);
                        if (i < node.ParameterList.Parameters.Count - 1)
                        {
                            Context.Writer.AppendComma();
                        }
                    }
                }
                Context.Writer.AppendSemiColon();
            }
            Context.Writer.AppendSemiColon();
        }
        else
        {
            Context.Writer.AppendKeyword("auto");
            Context.Writer.AppendSpace();
            Context.Writer.Append(name);
            Context.Writer.AppendOperator("=");
            Context.Writer.Append("[=] __device__ ");
            using (Context.Writer.Parentheses())
            {
                WriteLambdaParameters(node.ParameterList.Parameters, ref Context);
            }

            var returnTypeSymbol = methodSymbol?.ReturnType;
            if (returnTypeSymbol != null && returnTypeSymbol.SpecialType != SpecialType.System_Void)
            {
                Context.Writer.Append(" -> ");
                Context.Writer.Append(CudaTypeMapper.MapType(returnTypeSymbol));
            }

            Context.Writer.AppendSpace();
            if (node.Body != null)
            {
                VisitStatementOrBlock(node.Body);
            }
            else if (node.ExpressionBody != null)
            {
                using (Context.Writer.Scope())
                {
                    Context.Writer.AppendKeyword("return");
                    Context.Writer.AppendSpace();
                    Visit(node.ExpressionBody.Expression);
                    Context.Writer.AppendSemiColon();
                }
            }
            Context.Writer.AppendSemiColon();
        }
    }

    // --- Expressions ---

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        TranspileLambda(node.ParameterList.Parameters, node.Body);
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        TranspileLambda(new[] { node.Parameter }, node.Body);
    }

    public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
    {
        TranspileLambda(node.ParameterList?.Parameters ?? Enumerable.Empty<ParameterSyntax>(), node.Block);
    }

    private void TranspileLambda(IEnumerable<ParameterSyntax> parameters, CSharpSyntaxNode? body)
    {
        Context.Writer.Append("[=] __device__ ");
        using (Context.Writer.Parentheses())
        {
            WriteLambdaParameters(parameters, ref Context);
        }
        Context.Writer.AppendSpace();
        if (body is BlockSyntax block)
        {
            VisitStatementOrBlock(block);
        }
        else if (body is ExpressionSyntax expr)
        {
            using (Context.Writer.Scope())
            {
                Context.Writer.AppendKeyword("return");
                Context.Writer.AppendSpace();
                Visit(expr);
                Context.Writer.AppendSemiColon();
            }
        }
    }

    private static void WriteLambdaParameters(IEnumerable<ParameterSyntax> parameters, ref TranspileContext context)
    {
        var paramList = parameters as IList<ParameterSyntax> ?? parameters.ToList();
        for (var i = 0; i < paramList.Count; i++)
        {
            var p = paramList[i];
            var paramSymbol = context.SemanticModel.GetDeclaredSymbol(p);
            var typeSymbol = paramSymbol?.Type ?? (p.Type != null ? context.SemanticModel.GetTypeInfo(p.Type).Type : null);

            if (typeSymbol != null)
            {
                context.Writer.Append(CudaTypeMapper.MapType(typeSymbol));
            }
            else
            {
                context.Writer.Append("auto");
            }

            context.Writer.AppendSpace();
            context.Writer.Append(p.Identifier.Text);
            if (i < paramList.Count - 1)
            {
                context.Writer.AppendComma();
            }
        }
    }

    private class CheckRecursionWalker : CSharpSyntaxWalker
    {
        private readonly ISymbol _targetSymbol;
        private readonly SemanticModel _model;
        public bool FoundRecursion { get; private set; }

        private CheckRecursionWalker(ISymbol targetSymbol, SemanticModel model)
        {
            _targetSymbol = targetSymbol;
            _model = model;
        }

        public static bool IsRecursive(SyntaxNode node, ISymbol targetSymbol, SemanticModel model)
        {
            var walker = new CheckRecursionWalker(targetSymbol, model);
            walker.Visit(node);
            return walker.FoundRecursion;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbol = _model.GetSymbolInfo(node).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, _targetSymbol))
            {
                FoundRecursion = true;
                return;
            }
            base.VisitInvocationExpression(node);
        }
    }

    public override void VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (MacroTranspiler.TryTranspileMacro(node, ref Context))
        {
            return;
        }

        Visit(node.Expression);
        Context.Writer.AppendSemiColon();
    }

    public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        => ExpressionTranspilers.TranspileAssignmentExpression(node, ref Context);

    public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        => ExpressionTranspilers.TranspileBinaryExpression(node, ref Context);

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

        Context.Writer.Append(node.Identifier.Text);
    }

    public override void VisitLiteralExpression(LiteralExpressionSyntax node) =>
        Context.Writer.Append(node.Token.Text);

    public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileElementAccessExpression(node, ref Context);

    public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePrefixUnaryExpression(node, ref Context);

    public override void VisitCastExpression(CastExpressionSyntax node)
        => ExpressionTranspilers.TranspileCastExpression(node, ref Context);

    public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        => ExpressionTranspilers.TranspileParenthesizedExpression(node, ref Context);

    public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        => ExpressionTranspilers.TranspilePostfixUnaryExpression(node, ref Context);

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        => ExpressionTranspilers.TranspileMemberAccessExpression(node, ref Context);

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        => ExpressionTranspilers.TranspileInvocationExpression(node, ref Context);

    public override void VisitArgument(ArgumentSyntax node)
        => ExpressionTranspilers.TranspileArgument(node, ref Context);

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileObjectCreationExpression(node, ref Context);

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
        => ExpressionTranspilers.TranspileImplicitObjectCreationExpression(node, ref Context);
}
