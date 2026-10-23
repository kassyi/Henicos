using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace AbiTest.CopyDirection
{
    // One array per [In] / [Out] combination, and one with a user attribute that only shares the name InAttribute
    public static partial class CopyKernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void CopyKernel([In] float[] inOnly, [Out] float[] outOnly, [In, Out] float[] both, float[] neither,
            [LookalikeAttributes.In] float[] lookalike)
        {
            int i = ThreadIndex.X;
            outOnly[i] = inOnly[i] + both[i] + neither[i] + lookalike[i];
        }
    }
}

namespace AbiTest.LookalikeAttributes
{
    [System.AttributeUsage(System.AttributeTargets.Parameter)]
    public sealed class InAttribute : System.Attribute
    {
    }
}
