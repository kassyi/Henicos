using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

// Kernels that call the device functions of Kassyi.GSharp.Tests.DeviceLibrary, another assembly built with the CUDA
// dialect (#433): the native module imports what the library recorded, the managed fallback calls the C# methods
public static partial class CrossAssemblyKernels
{
    // A device function through a using directive (the managed body names it in full)
    [GpuKernel]
    public static void MuKernel(uint[] values, uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibMath.Mu(values[tid]);
    }

    // The same, written in full
    [GpuKernel]
    public static void QualifiedMuKernel(uint[] values, uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = Kassyi.GSharp.Tests.DeviceLibrary.LibMath.Mu(values[tid]);
    }

    // A struct of the library as a parameter and an argument
    [GpuKernel]
    public static void SumPairKernel(Pair[] pairs, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibMath.SumPair(pairs[tid]);
    }

    // A public function that calls an internal one of the library
    [GpuKernel]
    public static void TwiceKernel(int[] values, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibMath.Twice(values[tid]);
    }

    // The constructor and the computed property of a library struct
    [GpuKernel]
    public static void Vec2Kernel(float[] xs, float[] result)
    {
        int tid = ThreadIndex.X;
        var v = new Vec2(xs[tid], 2f);
        result[tid] = v.Sum;
    }

    // A library function that waits for the block twice: the CPU dialects suspend the kernel at each barrier, the
    // managed fallback runs the library's state machine
    [GpuKernel]
    public static void ExchangeKernel(int[] buffer, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibMath.Exchange(buffer, tid, tid * 10);
    }

    // A library function with a Float3 parameter, reached through another one: the kernel names no Float3
    [GpuKernel]
    public static void Length3Kernel(float[] xs, float[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = LibMath.Length3(xs[tid], 1f, 2f);
    }

    // The same function, with a Float3 of the kernel
    [GpuKernel]
    public static void Dot3Kernel(float[] xs, float[] result)
    {
        int tid = ThreadIndex.X;
        var v = new Float3(xs[tid], 1f, 2f);
        result[tid] = LibMath.Dot3(v);
    }

    // A library struct with a Float3 field
    [GpuKernel]
    public static void ParticleKernel(float[] xs, float[] result)
    {
        int tid = ThreadIndex.X;
        var p = new Particle(new Float3(xs[tid], 0f, 0f), 3f);
        result[tid] = p.Weight;
    }

    // A library constructor that calls a library function
    [GpuKernel]
    public static void ScaledKernel(int[] values, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = new Scaled(values[tid]).V;
    }

    // A library struct with LayoutKind.Explicit by value: its offsets come from the metadata (#736)
    [GpuKernel]
    public static void SwapPairKernel(SwapPair pair, int[] result)
    {
        result[0] = pair.A;
        result[1] = pair.B;
    }

    // A library struct with Pack = 1 by value
    [GpuKernel]
    public static void PackedPairKernel(PackedPair pair, int[] result)
    {
        result[0] = pair.Tag;
        result[1] = pair.Value;
    }

    // An array of a library struct with StructLayout.Size = 16: the elements sit 16 bytes apart (#736)
    [GpuKernel]
    public static void SizedKernel(Sized[] data, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = data[tid].A;
        data[tid].A = tid * 10;
    }

    // An array of a library union: the device reads the float's bits through the int and negates the float (#736)
    [GpuKernel]
    public static void IntFloatUnionKernel(IntFloatUnion[] data, int[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = data[tid].Bits;
        data[tid].Value = -data[tid].Value;
    }

    // Library structs that come after the structs of their fields and constructors
    [GpuKernel]
    public static void AlphaKernel(int[] values, int[] result)
    {
        int tid = ThreadIndex.X;
        var a = new Alpha(values[tid]);
        result[tid] = a.Probe * 100 + a.Inner.V;
    }
}
