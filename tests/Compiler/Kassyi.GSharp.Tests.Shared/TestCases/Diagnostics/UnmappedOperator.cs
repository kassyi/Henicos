using System.Diagnostics;
using System.Numerics;
using Kassyi.GSharp.Core;

namespace UnmappedOperatorTest
{
    public struct Counter
    {
        public int Value;

        public static Counter operator ++(Counter c) => new Counter { Value = c.Value + 1 };
        public static Counter operator --(Counter c) => new Counter { Value = c.Value - 1 };
    }

    public static class Checks
    {
        [Conditional("UNMAPPED_OPERATOR_CHECKS")]
        public static void Check(bool condition)
        {
        }
    }

    public static partial class UnmappedOperatorKernels
    {
        // Operators of System types that no dialect defines: GSH0060 at each operator token
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void SystemOperatorKernel(Quaternion[] q, Matrix4x4[] m, Complex[] c, Vector3[] v)
        {
            int t = ThreadIndex.X;
            q[t] = q[t] * q[t];
            m[t] = m[t] * m[t];
            c[t] = c[t] * c[t];
            v[t] = v[t] + v[t];
            Vector3 w = v[t];
            w += v[t];
            v[t] = -w;
        }

        // Increment and decrement of a struct of the project, prefix and postfix
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void StepOperatorKernel(Counter[] counters)
        {
            int t = ThreadIndex.X;
            Counter a = counters[t];
            a++;
            --a;
            counters[t] = a;
        }

        // Built-in operators, a span compared with null and an operator in a call that C# removes: no GSH0060
        [GpuKernel]
        [SkipSnapshotConsistency("Diagnostics case")]
        public static void BuiltInOperatorKernel(float[] f, int[] i, Vector3[] v)
        {
            int t = ThreadIndex.X;
            f[t] = -f[t] * 2f + 1f;
            i[t] += 3;
            i[t]++;
            Checks.Check(v[t] + v[t] == v[t] * 2f);
            System.Span<int> empty = default;
            i[t] += empty == null ? 1 : 0;
        }
    }
}
