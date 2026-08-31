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
    private int _divergenceDepth;

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
        _divergenceDepth = 0;
        s_pool.Add(this);
    }

    public override void VisitIfStatement(IfStatementSyntax node)
    {
        var isDivergent = false;
        if (node.Condition is not LiteralExpressionSyntax)
        {
            var constantValue = _context.SemanticModel.GetConstantValue(node.Condition);
            isDivergent = !constantValue.HasValue;
        }

        if (isDivergent)
        {
            _divergenceDepth++;
        }

        base.VisitIfStatement(node);
        if (isDivergent)
        {
            _divergenceDepth--;
        }
    }

    public override void VisitForEachStatement(ForEachStatementSyntax node)
    {
        _divergenceDepth++;
        ForEachNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitForEachStatement(node);
        _divergenceDepth--;
    }

    public override void VisitQueryExpression(QueryExpressionSyntax node)
    {
        LinqNotSupportedRule.Instance.Analyze(node, ref _context);
        base.VisitQueryExpression(node);
    }

    public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
    {
        ReservedIdentifierRule.Validate(node.Identifier.Text, node.Identifier.GetLocation(), _context.ReportDiagnostic);
        ImplicitVarNeedsSuffixRule.Instance.Analyze(node, ref _context);
        base.VisitVariableDeclarator(node);
    }

    public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
    {
        ReservedIdentifierRule.Validate(node.Identifier.Text, node.Identifier.GetLocation(), _context.ReportDiagnostic);
        base.VisitSingleVariableDesignation(node);
    }

    public override void VisitParameter(ParameterSyntax node)
    {
        ReservedIdentifierRule.Validate(node.Identifier.Text, node.Identifier.GetLocation(), _context.ReportDiagnostic);
        base.VisitParameter(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        ReservedIdentifierRule.Validate(node.Identifier.Text, node.Identifier.GetLocation(), _context.ReportDiagnostic);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        ReservedIdentifierRule.Validate(node.Identifier.Text, node.Identifier.GetLocation(), _context.ReportDiagnostic);
        base.VisitLocalFunctionStatement(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        GpuMacroArgumentRule.Instance.Analyze(node, ref _context);

        if (_context.SemanticModel.GetSymbolInfo(node).Symbol is IMethodSymbol invokedMethod)
        {
            if (invokedMethod is { Name: "SyncThreads", ContainingType.Name: "Gpu" } && _divergenceDepth > 0)
            {
                _context.ReportDiagnostic(Diagnostic.Create(
                    GSharpDiagnostics.SyncThreadsNotAllowedInDivergentPath,
                    node.GetLocation()));
            }
            else if (invokedMethod.Name == "WarpShuffle" &&
                     invokedMethod.ContainingType?.Name == "Gpu" &&
                     invokedMethod.ContainingNamespace?.ToDisplayString() == TypeConstants.CoreNamespace)
            {
                if (invokedMethod.TypeArguments.Length > 0 && !invokedMethod.TypeArguments[0].IsUnmanagedType)
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        GSharpDiagnostics.UnsupportedSyntax,
                        node.GetLocation(),
                        invokedMethod.TypeArguments[0].ToDisplayString()));
                }
            }
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        SharedMemoryForbiddenDisposableRule.Instance.Analyze(node, ref _context);
        base.VisitObjectCreationExpression(node);
    }

    public override void VisitImplicitObjectCreationExpression(ImplicitObjectCreationExpressionSyntax node)
    {
        SharedMemoryForbiddenDisposableRule.Instance.Analyze(node, ref _context);
        base.VisitImplicitObjectCreationExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        HostPropertyAccessRule.Instance.AnalyzeIdentifierName(node, ref _context);
        base.VisitIdentifierName(node);
    }

    public override void VisitTupleExpression(TupleExpressionSyntax node)
    {
        ValidateTupleType(node);
        base.VisitTupleExpression(node);
    }

    public override void VisitTupleType(TupleTypeSyntax node)
    {
        ValidateTupleType(node);
        base.VisitTupleType(node);
    }

    private void ValidateTupleType(SyntaxNode node)
    {
        var typeSymbol = _context.SemanticModel.GetTypeInfo(node).Type
                         ?? _context.SemanticModel.GetSymbolInfo(node).Symbol as ITypeSymbol;

        if (typeSymbol is INamedTypeSymbol { IsTupleType: true } named)
        {
            foreach (var element in named.TupleElements)
            {
                if (element.Type.IsReferenceType)
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        GSharpDiagnostics.TupleCannotContainReferenceTypes,
                        node.GetLocation(),
                        element.Type.ToDisplayString()));
                    break;
                }
            }
        }
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        HostPropertyAccessRule.Instance.AnalyzeMemberAccess(node, ref _context);
        base.VisitMemberAccessExpression(node);
    }
}

