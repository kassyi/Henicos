using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static class GpuConstantMacroNames
{
    public const string Width = "WIDTH_MACRO";
}

// Gpu.Constant writes the value of a constant string argument, the macro name; any other argument reports GSH0012
public static partial class GpuConstantArgumentKernels
{
    private const string Macro = "FOO_MACRO";

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConstantArgumentsKernel(int[] r)
    {
        r[0] = Gpu.Constant(nameof(GpuConstantMacroNames));
        r[1] = Gpu.Constant(Macro);
        r[2] = Gpu.Constant(GpuConstantMacroNames.Width);
        r[3] = Gpu.Constant("A" + "B");
        r[4] = Gpu.Constant("LITERAL_MACRO");
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void NonConstantArgumentKernel(int[] r, int which)
    {
        r[0] = Gpu.Constant(which == 0 ? "A" : "B");
    }
}
