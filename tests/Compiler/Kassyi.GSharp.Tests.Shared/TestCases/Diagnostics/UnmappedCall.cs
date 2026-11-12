using System;
using System.Collections.Immutable;
using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

[assembly: BclIntrinsicMap("UnmappedCallTest.HostBits", "Lzc", ["System.UInt32"], "__gsharp_lzc_32")]

namespace UnmappedCallTest
{
    public static class HostBits
    {
        public static int Lzc(uint x) => BitOperations.LeadingZeroCount(x);
    }

    public static partial class UnmappedCallKernels
    {
        // Methods of other assemblies that no dialect implements: GSH0109 at each call
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void UnmappedKernel(float[] f, int[] i, long[] l, short[] s)
        {
            int t = ThreadIndex.X;
            f[t] = float.Clamp(f[t], 0f, 1f);
            i[t] = int.Max(i[t], 3);
            l[t] = Math.Max(l[t], 3L);
            f[t] = MathF.Cbrt(f[t]);
            i[t] = int.Abs(i[t]);
            s[t] = short.Abs(s[t]);
        }

        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void VectorKernel(Vector3[] a, float[] r)
        {
            int t = ThreadIndex.X;
            r[t] = Vector3.Dot(a[t], a[t]);
        }

        // A macro whose body is a method group
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void MethodGroupBodyKernel(int[] r)
        {
            Gpu.SingleThread(Body);
        }

        [GpuDeviceFunction]
        public static void Body()
        {
        }

        // Mapped, emitted or written by a rule: no GSH0109
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void MappedKernel(int[] i, uint[] u, ulong[] l, ImmutableArray<int> imm)
        {
            int t = ThreadIndex.X;
            i[t] = Math.Abs(i[t]) + Math.Max(i[t], 2) + HostBits.Lzc(u[t]) + imm.AsSpan().Length;
            l[t] = GoldilocksArithmetic.GoldilocksPow(l[t], 2UL);
            i[t] = Local(i[t]);
            Gpu.SingleThread(() => Body());

            int Local(int v) => v + 1;
        }
    }
}
