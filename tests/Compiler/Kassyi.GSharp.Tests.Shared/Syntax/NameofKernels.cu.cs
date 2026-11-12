using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that use <c>nameof</c> where its value is not a string: the length of the name, and comparisons of constant
/// strings, which device code holds as the values C# gives them.
/// </summary>
public static partial class NameofKernels
{
    private const string Label = "label";

    // r[t] = inputs[t] + 13, the length of "NameofKernels"
    [GpuDeviceFunction]
    public static void AddNameLength(int t, int[] inputs, int[] r)
    {
        r[t] = inputs[t] + nameof(NameofKernels).Length;
    }

    [GpuKernel]
    public static void NameLengthKernel(int[] inputs, int[] r)
    {
        int t = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (t < inputs.Length)
        {
            AddNameLength(t, inputs, r);
        }
    }

    // r = [1, 0, 1, 3, 5, 3]
    [GpuKernel]
    public static void NameComparisonKernel(int[] r)
    {
        bool same = nameof(NameofKernels) == "NameofKernels";
        r[0] = same ? 1 : 0;
        r[1] = nameof(r) != "r" ? 1 : 0;
        r[2] = nameof(Label) == "Label" && Label != nameof(Label) ? 1 : 0;
        r[3] = (nameof(r) + "ab").Length;
        r[4] = Label.Length;
        r[5] = "abc".Length;
    }

    // r[t] = t + 22, the length of "NameAfterBarrierKernel", read after a barrier (a lowered body on the CPU dialects)
    [GpuKernel]
    public static void NameAfterBarrierKernel(int[] r)
    {
        int t = ThreadIndex.X;
        r[t] = t;
        Gpu.SyncThreads();
        r[t] += nameof(NameAfterBarrierKernel).Length;
    }
}
