namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports, in GPU code (GSH0003): the members of <c>System.Math</c> and <c>System.MathF</c> that the project's dialect
/// does not map to a native function (constants such as <c>Math.PI</c> and the calls of mapped members are allowed; a
/// mapped member used as a method group is not, as its native function is an overload set that converts to no delegate),
/// every member of <c>System.Threading.Interlocked</c> and <c>System.Numerics.BitOperations</c> (#570), and the host-only StackLinq
/// operators.
/// </summary>
internal sealed class ProhibitedApiRule :
    ISyntaxValidationRule<MemberAccessExpressionSyntax>,
    ISyntaxValidationRule<InvocationExpressionSyntax>,
    ISyntaxValidationRule<IdentifierNameSyntax>
{
    public static readonly ProhibitedApiRule Instance = new();
    public ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(GSharpDiagnostics.UnsupportedSyntax);

    private ProhibitedApiRule()
    {
    }

    public void Analyze(MemberAccessExpressionSyntax node, ref ValidationContext context)
    {
        var symbol = context.SemanticModel.GetSymbol(node);
        if (symbol is IMethodSymbol spanMethod && SpanWriteClassifier.ClassifyMethod(spanMethod) != SpanMemberKind.None)
        {
            return;
        }

        if (symbol == null && node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
        {
            symbol = context.SemanticModel.GetSymbol(invocation);
        }

        AnalyzeSymbol(symbol, node, ref context);
    }

    public void Analyze(InvocationExpressionSyntax node, ref ValidationContext context)
    {
        if (node.Expression is MemberAccessExpressionSyntax)
        {
            return;
        }

        var symbol = context.SemanticModel.GetSymbol(node);
        if (symbol is IMethodSymbol spanMethod && SpanWriteClassifier.ClassifyMethod(spanMethod) != SpanMemberKind.None)
        {
            return;
        }
        AnalyzeSymbol(symbol, node, ref context);
    }

    // A Math or MathF method named without its type (using static System.Math) and not called: a method group. A called
    // one is analyzed at its invocation, a qualified one at its member access
    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if ((node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node) ||
            (node.Parent is MemberAccessExpressionSyntax member && member.Name == node) ||
            context.SemanticModel.GetSymbol(node) is not IMethodSymbol { ContainingType: var type } method ||
            !type.IsSystemMathType())
        {
            return;
        }

        AnalyzeSymbol(method, node, ref context);
    }

    private static void AnalyzeSymbol(ISymbol? symbol, SyntaxNode locationNode, ref ValidationContext context)
    {
        if (symbol == null)
        {
            return;
        }

        if (symbol.ContainingType.IsSystemMathType())
        {
            // The dialect YAML decides per overload (Math.Abs(int) is mapped, Math.Abs(long) is not)
            if (symbol is not IFieldSymbol { IsConst: true } && !context.Dialect.TryMapIntrinsic(symbol, out _))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    locationNode,
                    $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (not mapped by {context.Dialect.Name})");
            }
            else if (symbol is IMethodSymbol && !IsInvoked(locationNode))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.UnsupportedSyntax,
                    locationNode,
                    $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (used as a method group)");
            }
        }
        else if (symbol is
                 {
                     ContainingType.Name: TypeConstants.InterlockedType or TypeConstants.BitOperationsType
                 } &&
                 (symbol.ContainingNamespace.IsSystemThreading() || symbol.ContainingNamespace.IsSystemOrNumerics()))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{symbol.Name}");
        }
        else if (symbol is IMethodSymbol method && StackLinqHelper.IsHostOnly(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.Name} (host-only StackLinq operator)");
        }
    }

    // Whether the node is a call or the method it calls (Math.Abs in Math.Abs(x)); otherwise a method group
    private static bool IsInvoked(SyntaxNode node) =>
        node is InvocationExpressionSyntax ||
        (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node);

    // Abs(long): the overload decides whether the dialect maps it
    private static string FormatMember(ISymbol symbol) =>
        symbol is IMethodSymbol method ? method.FormatSignature() : symbol.Name;
}
