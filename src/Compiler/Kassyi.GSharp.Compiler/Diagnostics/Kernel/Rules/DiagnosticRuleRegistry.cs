namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal static class DiagnosticRuleRegistry
{
    public static readonly ImmutableArray<IValidationRule> Rules = ImmutableArray.Create<IValidationRule>(
        CppReservedIdentifierRule.Instance,
        ForEachNotSupportedRule.Instance,
        GpuMacroArgumentRule.Instance,
        ImplicitVarNeedsSuffixRule.Instance,
        LinqNotSupportedRule.Instance,
        ManualEnumeratorRule.Instance,
        ParamsModifierNotSupportedRule.Instance,
        ProhibitedApiRule.Instance,
        StaticMemberAccessRule.Instance,
        UserDefinedOperatorRule.Instance
    );

    public static readonly ImmutableArray<DiagnosticDescriptor> AllSupportedDiagnostics = CreateAllSupportedDiagnostics();

    private static ImmutableArray<DiagnosticDescriptor> CreateAllSupportedDiagnostics()
    {
        var builder = ImmutableArray.CreateBuilder<DiagnosticDescriptor>();
        foreach (var rule in Rules)
        {
            foreach (var diag in rule.SupportedDiagnostics)
            {
                if (!builder.Contains(diag))
                {
                    builder.Add(diag);
                }
            }
        }
        return builder.ToImmutable();
    }
}
