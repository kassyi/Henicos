using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports, in GPU code (GSH0003): a member of <c>System.Math</c> or <c>System.MathF</c> that the project's dialect
/// maps to a native function, used as a method group (its native function is an overload set, which converts to no
/// delegate); a member of <c>System.Threading.Interlocked</c> or <c>System.Numerics.BitOperations</c> that
/// <c>[BclIntrinsicMap]</c> maps and that takes a <c>nint</c> / <c>nuint</c> or is used as a method group (#570); and
/// the host-only StackLinq operators.
/// </summary>
/// <remarks>
/// A member that neither the dialect nor <c>[BclIntrinsicMap]</c> maps is reported by <see cref="CallTargetRule"/>
/// (GSH0109), with every other method that GPU code cannot call.
/// </remarks>
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
        // [WHY] Span and array members classified by SpanWriteClassifier (including Unsupported) are skipped here
        // because the transpiler rules (SpanMemberTranspilerRule, MemberAccessExpressionTranspilerRule) report GSH0003
        // via TranspilerFailureCheck with exact member details (#731).
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

        // [WHY] Span and array members classified by SpanWriteClassifier (including Unsupported) are skipped here
        // because the transpiler rules (SpanMemberTranspilerRule, MemberAccessExpressionTranspilerRule) report GSH0003
        // via TranspilerFailureCheck with exact member details (#731).
        var symbol = context.SemanticModel.GetSymbol(node);
        if (symbol is IMethodSymbol spanMethod && SpanWriteClassifier.ClassifyMethod(spanMethod) != SpanMemberKind.None)
        {
            return;
        }
        AnalyzeSymbol(symbol, node, ref context);
    }

    // A Math, MathF, Interlocked or BitOperations method named without its type (using static) and not called: a method
    // group. A called one is analyzed at its invocation, a qualified one at its member access
    public void Analyze(IdentifierNameSyntax node, ref ValidationContext context)
    {
        if ((node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node) ||
            (node.Parent is MemberAccessExpressionSyntax member && member.Name == node) ||
            context.SemanticModel.GetSymbol(node) is not IMethodSymbol { ContainingType: var type } method ||
            !(type.IsSystemMathType() || IsInterlockedOrBitOperations(method)))
        {
            return;
        }

        AnalyzeSymbol(method, node, ref context);
    }

    private static void AnalyzeSymbol(ISymbol? symbol, SyntaxNode locationNode, ref ValidationContext context)
    {
        if (symbol is not IMethodSymbol method)
        {
            return;
        }

        if (method.ContainingType.IsSystemMathType())
        {
            AnalyzeMathMember(method, locationNode, ref context);
        }
        else if (IsInterlockedOrBitOperations(method))
        {
            AnalyzeMappedBclMember(method, locationNode, ref context);
        }
        else if (StackLinqHelper.IsHostOnly(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.Name} (host-only StackLinq operator)");
        }
    }

    // A mapped member (the dialect YAML decides per overload) named without a call; an unmapped one is GSH0109
    private static void AnalyzeMathMember(IMethodSymbol method, SyntaxNode locationNode, ref ValidationContext context)
    {
        if (!IsInvoked(locationNode) && context.Dialect.TryMapIntrinsic(method, out _))
        {
            ReportMethodGroup(method, locationNode, ref context);
        }
    }

    private static bool IsInterlockedOrBitOperations(ISymbol symbol) =>
        symbol is { ContainingType.Name: TypeConstants.InterlockedType or TypeConstants.BitOperationsType } &&
        (symbol.ContainingNamespace.IsSystemThreading() || symbol.ContainingNamespace.IsSystemOrNumerics());

    // [WHY] The transpiler converts the members of Interlocked and BitOperations that [BclIntrinsicMap] lists (Core
    // BclMappings.cs, read by BclIntrinsicMapBuilder) per overload. A mapped method used as a method group has no
    // function to convert to a delegate, as for Math.
    private static void AnalyzeMappedBclMember(IMethodSymbol method, SyntaxNode locationNode,
        ref ValidationContext context)
    {
        var map = BclIntrinsicMapBuilder.GetOrBuildMap(context.SemanticModel.Compilation);
        if (!map.ContainsKey(method.OriginalDefinition))
        {
            return;
        }

        if (TakesNativeInteger(method))
        {
            // A nint or nuint value does not build in GPU code yet (#983); the overloads are enabled there
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.ContainingType.Name}.{method.FormatSignature()} (nint and nuint are not supported)");
        }
        else if (!IsInvoked(locationNode))
        {
            ReportMethodGroup(method, locationNode, ref context);
        }
    }

    private static void ReportMethodGroup(IMethodSymbol method, SyntaxNode locationNode,
        ref ValidationContext context) =>
        context.ReportDiagnostic(
            GSharpDiagnostics.UnsupportedSyntax,
            locationNode,
            $"{method.ContainingType.Name}.{method.FormatSignature()} (used as a method group)");

    private static bool TakesNativeInteger(IMethodSymbol method) =>
        method.Parameters
            .AsStackEnumerable()
            .Any(static p => p.Type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr);

    // Whether the node is a call or the method it calls (Math.Abs in Math.Abs(x)); otherwise a method group
    private static bool IsInvoked(SyntaxNode node) =>
        node is InvocationExpressionSyntax ||
        (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node);
}
