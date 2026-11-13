using Kassyi.GSharp.Core;

namespace TestNamespace.Access;

// A private struct with an array field: the launchers and the marshaling methods name it. The kernels are as
// accessible as C# lets them be; the launchers are not capped by that
public static partial class PrivateStructKernels
{
    private struct Bag
    {
        public int[] Values;
        public int Scale;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    private static void ScaleKernel(Bag bag)
    {
        int i = ThreadIndex.X;
        bag.Values[i] *= bag.Scale;
    }
}

// An array of a private blittable struct
public static partial class PrivateBlittableKernels
{
    private struct Pair
    {
        public int A;
        public int B;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    private static void SumKernel(Pair[] pairs, int[] sums)
    {
        int i = ThreadIndex.X;
        Pair pair = pairs[i];
        Gpu.SyncThreads();
        sums[i] = pair.A + pair.B;
    }
}

// An internal struct and an internal kernel of a public class
internal struct Cell
{
    public int[] Values;
}

public static partial class InternalStructKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    internal static void FillKernel(Cell cell) => cell.Values[ThreadIndex.X] = 7;
}

// A struct of a private class, used by a kernel of a public class next to it
public static partial class OuterHost
{
    private static class Hidden
    {
        internal struct Q
        {
            public int[] Values;
        }
    }

    public static partial class Kernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        private static void CopyKernel(Hidden.Q q, int[] output) => output[ThreadIndex.X] = q.Values[ThreadIndex.X];
    }
}

// Every type public: the launchers stay public
public struct Open
{
    public int[] Values;
}

public static partial class OpenKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void OpenKernel(Open open) => open.Values[ThreadIndex.X] = 1;
}

// A private struct narrows the AotContexts class, so the typed pointer launchers of an overloaded kernel, which take
// GpuBuffer<AotContexts.PubR>, are narrowed with it
public struct PubR
{
    public int A;
}

public static partial class Mixed2
{
    private struct PrivS
    {
        public int[] V;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    private static void PrivK(PrivS s) => s.V[ThreadIndex.X] = 2;

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void PubK(ref PubR r) => r.A += 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void PubK(ref PubR r, int x) => r.A += x;

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ArrayK(PubR[] a) => a[ThreadIndex.X].A = 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Generator case")]
    public static void ArrayK(PubR[] a, int x) => a[ThreadIndex.X].A = x;
}
