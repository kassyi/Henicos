namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the check of the launch dimensions against the limits of every GPU (<c>LaunchLimits.CheckLaunch</c>) at
/// the start of a launch method, after the array arguments are checked, on CUDA and on both CPU paths (#799).
/// </summary>
internal static class LaunchLimitEmitter
{
    private const string LaunchLimitsType = "global::Kassyi.GSharp.Runtime.LaunchLimits";
    private const string GridBlockCount = GSharpInternalNames.Launcher.GridBlockCount;
    private const string Dimensions = "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ";
    private const string BlockDimensions = "blockDimX, blockDimY, blockDimZ";

    /// <summary>
    /// CPU: emits <c>long __gsharp_launch_block_count = LaunchLimits.CheckLaunch(...);</c>, before the managed and
    /// native paths branch; both run the grid from this count, which never wraps (#805).
    /// </summary>
    public static void WriteCpuCheck(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLine(
            $"long {GridBlockCount} = {LaunchLimitsType}.CheckLaunch({Dimensions}, {GetNameLiteral(kernel)});");

    /// <summary>
    /// CUDA: emits <c>LaunchLimits.CheckLaunch(...);</c>, so that the driver never gets dimensions it rejects.
    /// </summary>
    public static void WriteCudaCheck(SourceWriter writer, in KernelHostModel kernel) =>
        writer.AppendLine($"{LaunchLimitsType}.CheckLaunch({Dimensions}, {GetNameLiteral(kernel)});");

    /// <summary>
    /// Managed CPU path: emits <c>int __gsharp_context_count = LaunchLimits.GetManagedContextCount(...);</c> and
    /// <c>int __gsharp_total_blocks = (int)__gsharp_launch_block_count;</c>. The first throws when the contexts of
    /// the grid do not fit in one array, so the block count fits in an <see langword="int"/> after it.
    /// </summary>
    public static void WriteManagedCounts(SourceWriter writer, in KernelHostModel kernel)
    {
        writer.Append($"int {GSharpInternalNames.Launcher.ContextCount} = {LaunchLimitsType}.GetManagedContextCount");
        writer.AppendLine($"({GridBlockCount}, {BlockDimensions}, {GetNameLiteral(kernel)});");
        writer.AppendLine($"int {GSharpInternalNames.Launcher.TotalBlocks} = (int){GridBlockCount};");
    }

    // "<Type.Method>", the kernel's name in the exception messages
    private static string GetNameLiteral(in KernelHostModel kernel) =>
        SymbolDisplay.FormatLiteral(kernel.MessageName, quote: true);
}
