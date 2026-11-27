namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// The barrier models of the two CPU paths (<c>GSharpNativeBarrierModel</c>, <c>GSharpManagedBarrierModel</c>) and
/// whether the project builds them (<c>GSharpTargetDialect</c> is not CUDA).
/// </summary>
internal readonly record struct CpuBarrierModels(
    NativeBarrierModel Native,
    ManagedBarrierModel Managed,
    bool BuildsCpuPaths)
{
    public static CpuBarrierModels Read(AnalyzerConfigOptions globalOptions) => new(
        NativeBarrierModels.Read(globalOptions),
        ManagedBarrierModels.Read(globalOptions),
        !TranspilerDialects.IsCuda(TranspilerDialects.GetName(globalOptions)));

    /// <summary>
    /// Whether both CPU paths run every method they can on stacks: a method eligible on both never runs as a state
    /// machine.
    /// </summary>
    public bool BothOnStacks => Native == NativeBarrierModel.Stack && Managed == ManagedBarrierModel.Stack;
}
