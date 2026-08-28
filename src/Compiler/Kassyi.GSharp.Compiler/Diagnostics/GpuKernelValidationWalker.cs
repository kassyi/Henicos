using System.Collections.Concurrent;
using Kassyi.GSharp.Compiler.Diagnostics.Rules;

namespace Kassyi.GSharp.Compiler.Diagnostics;

/// <summary>Pooled syntax walker that performs zero-allocation validation of GPU kernel AST nodes.</summary>
internal sealed class GpuKernelValidationWalker : CSharpSyntaxWalker, IDisposable
{
    // [WHY] Reuses pooled walker instances to eliminate GC pressure during continuous background Roslyn analysis.
    private static readonly ConcurrentBag<GpuKernelValidationWalker> s_pool = [];

    private ValidationContext _context;
    private int _isRented;

    private GpuKernelValidationWalker()
    {
    }

    public static GpuKernelValidationWalker Rent(ValidationContext context)
    {
        if (!s_pool.TryTake(out var walker))
        {
            walker = new GpuKernelValidationWalker();
        }

        Volatile.Write(ref walker._isRented, 1);

        walker._context = context;
        return walker;
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

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        ForEachNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitForEachStatement(node);
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        LinqNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        ImplicitVarNeedsSuffixRule.Instance.Analyze(node, ref _context);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        CheckDelegateType(node.Type, node.GetLocation());
        base.VisitVariableDeclaration(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        if (node.Type != null)
        {
            CheckDelegateType(node.Type, node.GetLocation());
        }
        base.VisitParameter(node);
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        CheckClosureCaptures(node.Body ?? (SyntaxNode?)node.ExpressionBody, node.GetLocation());
        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
    {
        CheckClosureCaptures(node.Body ?? (SyntaxNode?)node.ExpressionBody, node.GetLocation());
        base.VisitParenthesizedLambdaExpression(node);
    }

    public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
    {
        CheckClosureCaptures(node.Body ?? (SyntaxNode?)node.ExpressionBody, node.GetLocation());
        base.VisitSimpleLambdaExpression(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        GpuMacroArgumentRule.Instance.Analyze(node, ref _context);
        base.VisitInvocationExpression(node);
    }

    private void CheckDelegateType(TypeSyntax typeSyntax, Location location)
    {
        var typeSymbol = _context.SemanticModel.GetTypeInfo(typeSyntax).Type;
        if (typeSymbol != null && IsDelegateType(typeSymbol))
        {
            _context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DelegateNotSupported, location, typeSymbol.ToDisplayString()));
        }
    }

    private static bool IsDelegateType(ITypeSymbol symbol)
    {
        return symbol.TypeKind == TypeKind.Delegate ||
               symbol.BaseType?.SpecialType == SpecialType.System_MulticastDelegate ||
               symbol.SpecialType == SpecialType.System_Delegate;
    }

    private void CheckClosureCaptures(SyntaxNode? closureBody, Location location)
    {
        if (closureBody == null) return;

        foreach (var id in closureBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = _context.SemanticModel.GetSymbolInfo(id).Symbol;
            if (symbol is ILocalSymbol or IParameterSymbol)
            {
                var declaringSyntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (declaringSyntax != null && !closureBody.Contains(declaringSyntax))
                {
                    var type = symbol is ILocalSymbol l ? l.Type : ((IParameterSymbol)symbol).Type;
                    if (type != null && type.IsReferenceType && type.SpecialType != SpecialType.System_Void && type.TypeKind != TypeKind.Array)
                    {
                        _context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.HeapClosureNotSupported, location, symbol.Name, type.ToDisplayString()));
                    }
                }
            }
        }
    }
}

