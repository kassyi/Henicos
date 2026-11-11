using Kassyi.GSharp.Compiler.Analysis;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

/// <summary>
/// Reports, in GPU code (GSH0003): the members of <c>System.Math</c> and <c>System.MathF</c> that the project's dialect
/// does not map to a native function (constants such as <c>Math.PI</c> and the calls of mapped members are allowed; a
/// mapped member used as a method group is not, as its native function is an overload set that converts to no
/// delegate), the members of <c>System.Threading.Interlocked</c> and <c>System.Numerics.BitOperations</c> that no
/// <c>[BclIntrinsicMap]</c> maps, takes a <c>nint</c> / <c>nuint</c> or is used as a method group (#570), and the
/// host-only StackLinq operators.
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
        if (symbol == null)
        {
            return;
        }

        if (symbol.ContainingType.IsSystemMathType())
        {
            AnalyzeMathMember(symbol, locationNode, ref context);
        }
        else if (IsInterlockedOrBitOperations(symbol))
        {
            AnalyzeMappedBclMember(symbol, locationNode, ref context);
        }
        else if (symbol is IMethodSymbol method && StackLinqHelper.IsHostOnly(method))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{method.Name} (host-only StackLinq operator)");
        }
    }

    // The dialect YAML decides per overload (Math.Abs(int) is mapped, Math.Abs(long) is not)
    private static void AnalyzeMathMember(ISymbol symbol, SyntaxNode locationNode, ref ValidationContext context)
    {
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

    private static bool IsInterlockedOrBitOperations(ISymbol symbol) =>
        symbol is { ContainingType.Name: TypeConstants.InterlockedType or TypeConstants.BitOperationsType } &&
        (symbol.ContainingNamespace.IsSystemThreading() || symbol.ContainingNamespace.IsSystemOrNumerics());

    // [WHY] The transpiler converts the members of Interlocked and BitOperations that [BclIntrinsicMap] lists (Core
    // BclMappings.cs, read by BclIntrinsicMapBuilder) per overload; the rule reads the same table, so a member is
    // rejected exactly when it has no mapping (#570). A mapped method used as a method group has no function to
    // convert to a delegate, as for Math.
    private static void AnalyzeMappedBclMember(ISymbol symbol, SyntaxNode locationNode, ref ValidationContext context)
    {
        var map = BclIntrinsicMapBuilder.GetOrBuildMap(context.SemanticModel.Compilation);
        if (symbol is not IMethodSymbol method || !map.ContainsKey(method.OriginalDefinition))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (no G# mapping)");
        }
        else if (TakesNativeInteger(method))
        {
            // A nint or nuint value does not build in GPU code yet (#983); the overloads are enabled there
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (nint and nuint are not supported)");
        }
        else if (!IsInvoked(locationNode))
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.UnsupportedSyntax,
                locationNode,
                $"{symbol.ContainingType.Name}.{FormatMember(symbol)} (used as a method group)");
        }
    }

    private static bool TakesNativeInteger(IMethodSymbol method) =>
        method.Parameters
            .AsStackEnumerable()
            .Any(static p => p.Type.SpecialType is SpecialType.System_IntPtr or SpecialType.System_UIntPtr);

    // Whether the node is a call or the method it calls (Math.Abs in Math.Abs(x)); otherwise a method group
    private static bool IsInvoked(SyntaxNode node) =>
        node is InvocationExpressionSyntax ||
        (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node);

    // Abs(long): the overload decides whether the dialect maps it
    private static string FormatMember(ISymbol symbol) =>
        symbol is IMethodSymbol method ? method.FormatSignature() : symbol.Name;
}
