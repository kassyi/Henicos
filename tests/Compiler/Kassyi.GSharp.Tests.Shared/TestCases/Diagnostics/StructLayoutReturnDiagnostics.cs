using System.Numerics;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace StructLayoutReturnDiagnosticsTest
{
    [StructLayout(LayoutKind.Explicit)]
    public struct Overlap { [FieldOffset(0)] public long A; [FieldOffset(4)] public int B; }

    public struct Priced { public int Id; public decimal Price; }

    public struct VecHolder { public float A; public Vector2 V; }

    [StructLayout(LayoutKind.Auto)]
    public struct AutoPair { public byte A; public long B; }

    public struct Plain { public int A; public float B; }

    // A returned struct comes back as an array with one element per thread (#704)
    public static partial class StructLayoutReturnKernels
    {
        [GpuKernel]
        public static Overlap OverlapKernel() => default;

        [GpuKernel]
        public static Priced PricedKernel() => default;

        [GpuKernel]
        public static VecHolder VecKernel() => default;

        [GpuKernel]
        public static AutoPair AutoKernel() => default;

        [GpuKernel]
        public static Plain PlainKernel() => default;

        [GpuKernel]
        public static BrokenUnionResult BrokenUnionKernel() => default;

        [GpuKernel]
        public static System.DateTime DateTimeKernel() => default;

        [GpuDeviceFunction]
        public static (int, System.Guid) GuidTupleDeviceFunction() => default;
    }

    [StructLayout(LayoutKind.Sequential, Size = 5)]
    public struct Size5 { public int A; public byte B; }

    public struct ShiftedFields { public Size5 First; public byte Second; }

    [StructLayout(LayoutKind.Explicit)]
    public struct BrokenUnionResult { [FieldOffset(0)] public long RawBits; [FieldOffset(0)] public ShiftedFields Shifted; }
}
