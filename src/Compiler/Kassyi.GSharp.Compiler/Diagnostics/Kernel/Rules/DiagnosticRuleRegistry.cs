namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;

internal static class DiagnosticRuleRegistry
{
    public static readonly ImmutableArray<IValidationRule> Rules = ImmutableArray.Create<IValidationRule>(
        CallTargetRule.Instance,
        CopyAsyncRule.Instance,
        CppReservedIdentifierRule.Instance,
        FileLocalTypeRule.Instance,
        ForEachNotSupportedRule.Instance,
        GpuFinalizerRule.Instance,
        GpuMacroArgumentRule.Instance,
        HeapObjectTypeRule.Instance,
        ImplicitVarNeedsSuffixRule.Instance,
        ImportedDeviceCodeRule.Instance,
        IteratorConsumptionRule.Instance,
        KernelSignatureRule.Instance,
        LinqNotSupportedRule.Instance,
        ManualEnumeratorRule.Instance,
        ManualUnmanagedAllocationRule.Instance,
        OversizedSharedMemoryTargetRule.Instance,
        ParallelAggregateRule.Instance,
        ParamsModifierNotSupportedRule.Instance,
        ProhibitedApiRule.Instance,
        ReadOnlyArrayWriteRule.Instance,
        RuntimeOnlyTypeRule.Instance,
        SharedMemoryDisposableRule.Instance,
        SharedMemorySizeRule.Instance,
        SharedMemoryWithoutCountRule.Instance,
        StackArrayEscapeRule.Instance,
        StaticMemberAccessRule.Instance,
        StructLayoutRule.Instance,
        UnsupportedConstructRule.Instance,
        UserDefinedOperatorRule.Instance,
        WarpShuffleWidthRule.Instance,
        WmmaRule.Instance
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
