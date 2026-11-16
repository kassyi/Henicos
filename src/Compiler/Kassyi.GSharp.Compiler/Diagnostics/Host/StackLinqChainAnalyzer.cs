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

        // 1. Syntactic prefilter before semantic binding
        if (invocation.Expression is not (MemberAccessExpressionSyntax or IdentifierNameSyntax or GenericNameSyntax))
        {
            return;
        }

        var parentNode = StripParenthesesUp(invocation);
        if (parentNode.Parent is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression == parentNode &&
            memberAccess.Parent is InvocationExpressionSyntax)
        {
            return;
        }

        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        // 2. Initial symbol check: must be a StackLinq method
        if (semanticModel.GetSymbol(invocation, cancellationToken) is not IMethodSymbol initialSymbol ||
            !IsStackLinqStep(initialSymbol))
        {
            return;
        }

        // 3. Only trigger on the outermost invocation to avoid redundant reports
        if (IsEnclosedInStackLinqCall(invocation, semanticModel, cancellationToken))
        {
            return;
        }

        // 4. Count chain depth walking upstream
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

            if (!StackLinqHelper.TryGetSourceExpression(currentInvocation, symbol, out currentExpression))
            {
                break;
            }
        }

        if (depth > MaxChainDepth)
        {
            context.ReportDiagnostic(GSharpDiagnostics.StackLinqChainTooDeep, invocation.GetLocation(), MaxChainDepth);
        }
    }

    private static bool IsStackLinqStep(IMethodSymbol symbol) =>
        StackLinqHelper.IsStackLinqMethod(symbol);

    private static bool IsEnclosedInStackLinqCall(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var parentNode = StripParenthesesUp(invocation);
        return IsReceiverOfStackLinqCall(parentNode, semanticModel, cancellationToken) ||
               IsSourceArgumentOfStaticStackLinqCall(parentNode, semanticModel, cancellationToken);
    }

    private static bool IsReceiverOfStackLinqCall(
        ExpressionSyntax parentNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Case A: Receiver of a member access in an outer StackLinq call:
        // inner.Select(...) or (inner).Select(...)
        if (parentNode.Parent is MemberAccessExpressionSyntax
            {
                Parent: InvocationExpressionSyntax memberParentInvocation
            } memberAccess &&
            memberAccess.Expression == parentNode)
        {
            if (semanticModel.GetSymbol(memberParentInvocation, cancellationToken) is
                IMethodSymbol memberParentSymbol &&
                IsStackLinqStep(memberParentSymbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSourceArgumentOfStaticStackLinqCall(
        ExpressionSyntax parentNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        // Case B: Source argument of a static outer StackLinq call:
        // StackLinqExtensions.Where(inner, ...)
        if (parentNode.Parent is ArgumentSyntax arg &&
            arg.Parent is ArgumentListSyntax { Parent: InvocationExpressionSyntax argParentInvocation })
        {
            if (semanticModel.GetSymbol(argParentInvocation, cancellationToken) is
                IMethodSymbol argParentSymbol &&
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
