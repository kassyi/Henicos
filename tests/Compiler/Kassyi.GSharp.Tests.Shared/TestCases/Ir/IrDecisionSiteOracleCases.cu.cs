#nullable enable
using Kassyi.GSharp.Core;

// Kernels whose error sites IrDecisionSiteOracleTests checks against the site IDs the syntax path generates; the test
// compiles this file under the fixed name SiteOracleKernels.cu.cs, the name the IDs hash (#1561). They hold a receiver
// or an argument written with ! and parentheses that the split moves into a temporary, a receiver proven by Gpu.NotNull
// that the split moves, an argument converted by a user-defined implicit operator, and a generic extension method
// called on its receiver.

public struct OracleHolder { public int[] Data; }

public struct OracleHolderWrap
{
    public int[] Data;
    public static implicit operator OracleHolder(OracleHolderWrap w) => new OracleHolder { Data = w.Data };
}

public struct OracleArrayWrap
{
    public int[] Values;
    public static implicit operator int[](OracleArrayWrap w) => w.Values;
}

public static partial class OracleExtensions
{
    [GpuDeviceFunction]
    public static int DivG<T>(this T[] x, int a, int b) => x.Length + a / b;

    [GpuDeviceFunction]
    public static int DivN(this int[] x, int a, int b) => x.Length + a / b;
}

[SkipSnapshotConsistency("An IR test input: Pick returns an array and DivG is generic, which the C++ build rejects")]
public static partial class SiteOracleKernels
{
    [GpuDeviceFunction]
    private static int ElementOf(int[] values, int index) => values[index];

    [GpuDeviceFunction]
    private static int OfHolder(OracleHolder holder, int index) => holder.Data[index];

    [GpuDeviceFunction]
    private static int[] Pick(int[] a) => a;

    [GpuDeviceFunction]
    private static OracleHolder PickH(OracleHolder h) => h;

    [GpuDeviceFunction]
    private static int Bar()
    {
        Gpu.SyncThreads();
        return 0;
    }

    [GpuKernel]
    public static void SpillKernel(int[]? b, int[]? a, int[] r)
    {
        r[0] = ElementOf(Pick(b)!, Bar());
        r[1] = ElementOf((a!), Bar());
        r[2] = (Pick(b)!)[Bar()];
        r[3] = (a!)[Bar()];
        r[4] = (Pick(b)!).Length + Bar();
        r[5] = ElementOf((Pick(b))!, Bar());
        (Pick(b)!)[Bar()] = 7;
    }

    [GpuKernel]
    public static void ProvenSpillKernel(int[]? b, int[] r)
    {
        r[0] = Gpu.NotNull(Pick(b))[Bar()];
        r[1] = ElementOf(Gpu.NotNull(Pick(b)), Bar());
        r[2] = Gpu.NotNull(Pick(b)).Length + Bar();
        Gpu.NotNull(Pick(b))[Bar()] = 7;
        r[3] = Pick(b)[Bar()];
    }

    [GpuKernel]
    public static void ParenthesizedSpillKernel(int[]? b, OracleHolder h, int[] r)
    {
        (Pick(b))[Bar()]++;
        r[0] = (PickH(h).Data)[Bar()];
        r[1] = ElementOf((Pick(b)), Bar());
        r[2] = OfHolder((PickH(h)), Bar());
        r[3] = ((Pick(b)))[Bar()];
        r[4] = ((Pick(b))!)[Bar()];
    }

    [GpuKernel]
    public static void ShuffleKernel(int[]? b, int[] a, int[] r, uint m, int v)
    {
        (Pick(b)!)[0] += Warp.ShuffleSync(m, v, 1);
        r[0] = ElementOf((Pick(b)!), Warp.ShuffleSync(m, v, 1));
        Gpu.NotNull(a)[1] += Warp.ShuffleSync(m, v, 1);
        Gpu.NotNull(Pick(b))[2] += Warp.ShuffleSync(m, v, 1);
    }

    [GpuKernel]
    public static void ConversionKernel(OracleArrayWrap w, OracleHolderWrap hw, int i, int[] r)
    {
        r[0] = ElementOf((w!), i);
        r[1] = OfHolder((hw!), i);
    }

    [GpuKernel]
    public static void RoutedKernel(int[] x, int a, int b, int[] r)
    {
        r[0] = x.DivG(a, b);
        r[1] = x.DivN(a, b);
        r[2] = OracleExtensions.DivG(x, a, b);
    }
}

// The length of a stack array local whose size is not a constant, and of one declared outside a local declaration
// statement: the syntax path writes both from the declaration
public static partial class StackLengthOracleKernels
{
    [GpuKernel]
    public static void StackLengthKernel(int n, int[] r)
    {
        int[] sized = new int[n];
        r[0] = sized.Length;
        for (int[] looped = new int[4]; r[1] < 1; r[1]++)
        {
            r[2] = looped.Length;
        }
    }
}
