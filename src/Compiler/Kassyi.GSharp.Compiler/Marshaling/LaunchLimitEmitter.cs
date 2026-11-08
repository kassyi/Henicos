namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the check of the launch dimensions against the limits of every GPU (<c>LaunchLimits.CheckLaunch</c>) at the start
/// of a launch method, after the array arguments are checked, on CUDA and on both CPU paths (#799).
/// </summary>
internal static class LaunchLimitEmitter
{
    private const string LaunchLimitsType = "global::Kassyi.GSharp.Runtime.LaunchLimits";
    private const string GridBlockCount = GSharpInternalNames.Launcher.GridBlockCount;
    private const string Dimensions = "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ";

    /// <summary>
    /// CPU: emits <c>long __gsharp_grid_block_count = LaunchLimits.CheckLaunch(...);</c>, before the managed and native
    /// paths branch; both run the grid from this count, which never wraps (#805).
    /// </summary>
    public static void WriteCpuCheck(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLine(
            $"long {GridBlockCount} = {LaunchLimitsType}.CheckLaunch({Dimensions}, {SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)});");

    /// <summary>CUDA: emits <c>LaunchLimits.CheckLaunch(...);</c>; the driver gets the dimensions it would reject otherwise.</summary>
    public static void WriteCudaCheck(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLine(
            $"{LaunchLimitsType}.CheckLaunch({Dimensions}, {SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)});");

    /// <summary>
    /// Managed CPU path: emits <c>int __gsharp_context_count = LaunchLimits.GetManagedContextCount(...);</c> and
    /// <c>int __gsharp_total_blocks = (int)__gsharp_grid_block_count;</c>; the first throws when the contexts of the grid
    /// do not fit in one array, so the block count fits in an <see langword="int"/> after it.
    /// </summary>
    public static void WriteManagedCounts(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.AppendLine(
            $"int {GSharpInternalNames.Launcher.ContextCount} = {LaunchLimitsType}.GetManagedContextCount({GridBlockCount}, blockDimX, blockDimY, blockDimZ, {SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true)});");
        writer.AppendLine($"int {GSharpInternalNames.Launcher.TotalBlocks} = (int){GridBlockCount};");
    }
}
