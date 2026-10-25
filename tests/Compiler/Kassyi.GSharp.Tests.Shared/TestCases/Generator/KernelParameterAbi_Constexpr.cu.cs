using System;
using Kassyi.GSharp.Core;

namespace AbiTest.Constexpr
{
    public enum SampleMode
    {
        Add,
        Multiply
    }

    public static partial class ConstexprAbiKernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void ConstexprKernel(
            [GpuConstexpr(2, 4)] int realInt,
            [GpuConstexpr(0.5, 1.5)] float realFloat,
            [GpuConstexpr] bool realBool,
            [GpuConstexpr] SampleMode realEnum,
            [LookalikeAttributes.Constant] int userConstant,
            [LookalikeAttributes.GpuConstexpr] int userGpuConstexpr,
            int plainInt)
        {
        }
    }
}

namespace AbiTest.LookalikeAttributes
{
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ConstantAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class GpuConstexprAttribute : Attribute
    {
    }
}
