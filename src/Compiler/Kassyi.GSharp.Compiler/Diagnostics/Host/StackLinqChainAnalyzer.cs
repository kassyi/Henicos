using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Kassyi.GSharp.Compiler.Transpiler.Rules.InvocationRules.StackLinq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class StackLinqChainAnalyzer : DiagnosticAnalyzer
{
    private const int MaxChainDepth = 50;

    public static string DiagnosticId => GSharpDiagnostics.StackLinqChainTooDeep.Id;

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.StackLinqChainTooDeep);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        // 1. Initial symbol check: must be a StackLinq method
        if (semanticModel.GetSymbol(invocation, cancellationToken) is not IMethodSymbol initialSymbol ||
            !IsStackLinqStep(initialSymbol))
        {
            return;
        }

        // 2. Only trigger on the outermost invocation to avoid redundant reports
        if (IsEnclosedInStackLinqCall(invocation, semanticModel, cancellationToken))
        {
            return;
        }

        // 3. Count chain depth walking upstream
        var depth = 0;
        ExpressionSyntax? currentExpression = invocation;

        while (currentExpression is not null)
        {
            currentExpression = StripParentheses(currentExpression);
            if (currentExpression is not InvocationExpressionSyntax currentInvocation)
            {
                break;
            }

            if (semanticModel.GetSymbol(currentInvocation, cancellationToken) is not IMethodSymbol symbol ||
                !IsStackLinqStep(symbol))
            {
                break;
            }

            depth++;

            if (!TryGetSourceExpression(currentInvocation, symbol, out currentExpression))
            {
                break;
            }
        }

        if (depth > MaxChainDepth)
        {
            context.ReportDiagnostic(GSharpDiagnostics.StackLinqChainTooDeep, invocation.GetLocation(), MaxChainDepth);
        }
    }

    private static bool IsStackLinqStep(IMethodSymbol symbol)
    {
        if (StackLinqHelper.IsStackLinqMethod(symbol))
        {
            var containingType = symbol.ContainingType;
            if (containingType is not null && StackLinqHelper.IsStackLinqType(containingType))
            {
                if (containingType.Name is "StackLinqExtensions" or "StackEnumerable" or "ParallelStackEnumerable")
                {
                    return true;
                }
            }

            if (StackLinqHelper.IsStackLinqType(symbol.ReturnType))
            {
                return true;
            }
        }
        else if (StackLinqHelper.IsStackLinqType(symbol.ReturnType))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetSourceExpression(
        InvocationExpressionSyntax invocation,
        IMethodSymbol symbol,
        [NotNullWhen(true)] out ExpressionSyntax? sourceExpression)
    {
        // Extension method in extension form OR instance method (e.g. OfType<TResult>())
        if (symbol.ReducedFrom is not null || !symbol.IsStatic)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                sourceExpression = memberAccess.Expression;
                return true;
            }

            sourceExpression = null;
            return false;
        }

        // Static method invocation (e.g. StackLinqExtensions.Where(source, ...))
        if (symbol.IsStatic && symbol.ReducedFrom is null)
        {
            if (StackLinqHelper.TryFindArgument(invocation, symbol, 0, out var sourceArg))
            {
                sourceExpression = sourceArg.Expression;
                return true;
            }
        }

        sourceExpression = null;
        return false;
    }

    private static bool IsEnclosedInStackLinqCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var parentNode = StripParenthesesUp(invocation);

        // Case A: Receiver of a member access in an outer StackLinq call:
        // inner.Select(...) or (inner).Select(...)
        if (parentNode.Parent is MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax memberParentInvocation } memberAccess &&
            memberAccess.Expression == parentNode)
        {
            if (semanticModel.GetSymbol(memberParentInvocation, cancellationToken) is IMethodSymbol memberParentSymbol &&
                IsStackLinqStep(memberParentSymbol))
            {
                return true;
            }
        }

        // Case B: Source argument of a static outer StackLinq call:
        // StackLinqExtensions.Where(inner, ...)
        if (parentNode.Parent is ArgumentSyntax arg &&
            arg.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax argParentInvocation })
        {
            if (semanticModel.GetSymbol(argParentInvocation, cancellationToken) is IMethodSymbol argParentSymbol &&
                IsStackLinqStep(argParentSymbol) &&
                argParentSymbol.IsStatic &&
                argParentSymbol.ReducedFrom is null &&
                StackLinqHelper.TryFindArgument(argParentInvocation, argParentSymbol, 0, out var sourceArg) &&
                sourceArg == arg)
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static ExpressionSyntax StripParenthesesUp(ExpressionSyntax expression)
    {
        var current = expression;
        while (current.Parent is ParenthesizedExpressionSyntax parenthesized)
        {
            current = parenthesized;
        }

        return current;
    }
}
