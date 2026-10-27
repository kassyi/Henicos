using Kassyi.GSharp.Core;
using ManagedQualificationCase.Types;
using static System.Math;
using static ManagedQualificationCase.CaseMath;
using CaseAlias = ManagedQualificationCase.CaseMath;
using Ints = int[];
using unsafe IntPointer = int*;
using Pair = (int A, int B);

namespace ManagedQualificationCase.Types
{
    public struct CasePair
    {
        public int A;
        public int B;
    }

    public static class CaseExtensions
    {
        [GpuDeviceFunction]
        public static int Doubled(this int x) => x * 2;

        [GpuDeviceFunction]
        public static void Bump(ref this int x) => x++;
    }
}

namespace ManagedQualificationCase
{
    public static partial class CaseMath
    {
        [GpuDeviceFunction]
        public static int Triple(int x) => x * 3;
    }
}

// A child namespace of ManagedQualificationCase, so CaseMath also resolves without a using directive in the generated
// file; the names inside nameof and an interpolated string rely on that.
namespace ManagedQualificationCase.Kernels
{
    public static partial class CaseKernels
    {
        [GpuKernel]
        public static unsafe void QualifyKernel(int[] values, int[] result)
        {
            int tid = ThreadIndex.X;
            CasePair pair = new CasePair { A = values[tid], B = Triple(values[tid]) };
            int bumped = values[tid];
            bumped.Bump();
            Gpu.SyncThreads();
            Ints copy = values;
            Pair tuple = (copy[tid], 2);
            IntPointer pointer = null;
            var isPair = pair is CasePair matched && matched.A >= 0;
            var sameType = typeof(CasePair) == typeof(CasePair);
            result[tid] = Max(pair.A, pair.B) + CaseAlias.Triple(1) + nameof(CaseAlias).Length +
                          $"{CaseMath.Triple(2)}".Length + values[tid].Doubled() + bumped + tuple.A + tuple.B +
                          (pointer == null ? 1 : 0) + (isPair ? 1 : 0) + (sameType ? 1 : 0);
        }
    }
}
