using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace StructLayoutDiagnosticsTest
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Overlap { [FieldOffset(0)] public long A; [FieldOffset(4)] public int B; }

    [StructLayout(LayoutKind.Explicit)]
    public struct Misaligned { [FieldOffset(0)] public byte A; [FieldOffset(1)] public int B; }

    public struct Named { public int Id; public string Name; }

    public struct Priced { public int Id; public decimal Price; }

    public struct VecHolder { public float A; public Vector2 V; }

    public struct Elem { public byte A; public Float2 V; }

    [InlineArray(2)]
    public struct Elems { private Elem _element0; }

    public struct InlineOuter { public int Tag; public Elems Items; }

    public struct Bag { public int N; public int[] Data; }

    [StructLayout(LayoutKind.Explicit)]
    public struct Union { [FieldOffset(0)] public int I; [FieldOffset(0)] public float F; }

    public struct Plain { public int A; public float B; }

    // D1 to D4 at the kernel parameter or local type; the last kernels report nothing
    public static partial class StructLayoutDiagnosticsKernels
    {
        [GpuKernel]
        public static void OverlapKernel(Overlap o, int[] r) { r[0] = o.B; }

        [GpuKernel]
        public static void OverlapArrayKernel(Overlap[] o, int[] r) { r[0] = o[0].B; }

        [GpuKernel]
        public static void MisalignedKernel(ref Misaligned m, int[] r) { r[0] = m.B; }

        [GpuKernel]
        public static void NamedKernel(Named n, int[] r) { r[0] = n.Id; }

        [GpuKernel]
        public static void OverlapLocalKernel(int[] r)
        {
            Overlap local = default;
            r[0] = local.B;
        }

        [GpuKernel]
        public static void PricedKernel(Priced p, int[] r) { r[0] = p.Id; }

        [GpuKernel]
        public static void VecArrayKernel(VecHolder[] h, float[] r) { r[0] = h[0].V.Y; }

        [GpuKernel]
        public static void InlineOuterKernel(ref InlineOuter x, int[] r) { r[0] = x.Items[1].A; }

        [GpuKernel]
        public static void BagArrayKernel(Bag[] b, int[] r) { r[0] = b[0].N; }

        [GpuKernel]
        public static void QuietKernel(Union u, Union[] us, Plain[] ps, VecHolder v, int[] r) { r[0] = u.I + us[0].I + ps[0].A; }
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct Adjacent { [FieldOffset(0)] public int A; [FieldOffset(4)] public int B; }

    public struct Wrapper { public int Tag; public Priced Inner; }

    [StructLayout(LayoutKind.Auto)]
    public struct AutoPair { public byte A; public long B; }

    public struct WithArray { public int[] Data; }

    [InlineArray(2)]
    public struct WithArrays { private WithArray _element0; }

    public struct ArraysHolder { public WithArrays Items; }

    public struct WideFirst { public Float4 W; public Float2 V; public float A; public float B; }

    public struct VectorFirst { public Vector2 V; public float A; public float B; }

    // GSH0080 on both dialects and GSH0081 on CUDA; the first and the last kernel report nothing
    public static partial class StructLayoutDialectKernels
    {
        [GpuKernel]
        public static void AdjacentKernel(Adjacent a, Adjacent[] b, int[] r) { r[0] = a.B + b[0].B; }

        [GpuKernel]
        public static void WrapperKernel(Wrapper w, int[] r) { r[0] = w.Tag; }

        [GpuKernel]
        public static void AutoArrayKernel(AutoPair[] a, int[] r) { r[0] = a[0].A; }

        [GpuKernel]
        public static void BagByValueKernel(Bag b, int[] r) { r[0] = b.N; }

        [GpuKernel]
        public static void BagByRefKernel(ref Bag b, int[] r) { r[0] = b.N; }

        [GpuKernel]
        public static void ArraysHolderKernel(ref ArraysHolder h, int[] r) { r[0] = h.Items[0].Data[0]; }

        [GpuKernel]
        public static void AlignedVectorsKernel(WideFirst[] w, VectorFirst[] v, float[] r) { r[0] = w[0].A + v[0].A; }
    }
}
