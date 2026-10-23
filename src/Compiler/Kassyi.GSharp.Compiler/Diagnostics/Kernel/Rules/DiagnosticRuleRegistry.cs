namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal static class DiagnosticRuleRegistry
{
    public static readonly ImmutableArray<IValidationRule> Rules = ImmutableArray.Create<IValidationRule>(
        CppReservedIdentifierRule.Instance,
        ForEachNotSupportedRule.Instance,
        GpuMacroArgumentRule.Instance,
        ImplicitVarNeedsSuffixRule.Instance,
        KernelSignatureRule.Instance,
        LinqNotSupportedRule.Instance,
        ManualEnumeratorRule.Instance,
        ManualUnmanagedAllocationRule.Instance,
        ParamsModifierNotSupportedRule.Instance,
        ProhibitedApiRule.Instance,
        SharedMemorySizeRule.Instance,
        StaticMemberAccessRule.Instance,
        UnsupportedConstructRule.Instance,
        UserDefinedOperatorRule.Instance
    );

    public static readonly ImmutableArray<DiagnosticDescriptor> AllSupportedDiagnostics = CreateAllSupportedDiagnostics();

    private static ImmutableArray<DiagnosticDescriptor> CreateAllSupportedDiagnostics()
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>();
        Rules
            .AsStackEnumerable()
            .SelectMany(static rule => rule.SupportedDiagnostics
                .AsStackEnumerable()
            ).AddIf(builder, builder, static (diag, added) => !added.Contains(diag));
        return builder.ToImmutable();
    }
}
