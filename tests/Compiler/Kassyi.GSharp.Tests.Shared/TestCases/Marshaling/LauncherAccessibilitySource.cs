using Kassyi.GSharp.Core;

namespace Access;

public struct PubR
{
    public int A;
}

public struct PubS
{
    public int[] V;
}

// One kernel of a private struct narrows the AotContexts class; the pointer launchers that name it follow
public static partial class Mixed
{
    private struct PrivS
    {
        public int[] V;
    }

    [GpuKernel]
    private static void PrivK(PrivS s) => s.V[ThreadIndex.X] = 2;

    // Overloads: the untyped Ptr launcher is private, the typed one takes GpuBuffer<AotContexts.PubR>
    [GpuKernel]
    public static void RefK(ref PubR r) => r.A += 1;

    [GpuKernel]
    public static void RefK(ref PubR r, int x) => r.A += x;

    // The untyped Ptr launcher takes the shadow struct AotContexts.PubS
    [GpuKernel]
    public static void ShadowK(PubS s) => s.V[ThreadIndex.X] = 1;

    // Neither launcher names AotContexts
    [GpuKernel]
    public static void ArrayK(int[] a, int n) => a[ThreadIndex.X] = n;

    // Overloads over arrays of a public blittable struct: the typed launcher takes GpuBuffer<AotContexts.PubR>
    [GpuKernel]
    public static void ArrayOfK(PubR[] a) => a[ThreadIndex.X].A = 1;

    [GpuKernel]
    public static void ArrayOfK(PubR[] a, int x) => a[ThreadIndex.X].A = x;
}

// The same public kernels without the private struct keep every launcher public
public static partial class Open
{
    [GpuKernel]
    public static void RefK(ref PubR r) => r.A += 1;

    [GpuKernel]
    public static void RefK(ref PubR r, int x) => r.A += x;

    [GpuKernel]
    public static void ShadowK(PubS s) => s.V[ThreadIndex.X] = 1;
}

// The state machine of a kernel whose context holds a private struct
public static partial class Frames
{
    private struct Local
    {
        public int A;
    }

    [GpuKernel]
    public static void PublicKernel(int[] a) => a[ThreadIndex.X] = 1;

    [GpuKernel]
    private static void PrivateParameterKernel(Local l, int[] a) => a[ThreadIndex.X] = l.A;
}
