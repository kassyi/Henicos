using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

public class GEN_33_StructLayoutTests
{
    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-33")]
    public void Test_GEN33_StructLayout_IsMaintained_InGpuKernel()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        var data = new CustomLayoutStruct[1]
        {
            new CustomLayoutStruct { A = 5, B = 10.0, C = 3.0f }
        };

        // SourceGenerator should emit this wrapper class and method.
        GEN_33_StructLayoutKernelsWrapper.LaunchModifyStructLayoutKernel(
            data,
            blockDimX: 1
        );

        Assert.Equal(15, data[0].A);
        Assert.Equal(20.0, data[0].B);
        Assert.Equal(2.0f, data[0].C);
    }

    [SkippableFact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "GEN-33")]
    public void Test_GEN33_PackedInlineArray_IsMaintained_InGpuKernel()
    {
        ExecutionCapabilityChecker.EnsureGpuExecutionCapability();
        var data = new StructWithPackedInlineArray[1];
        data[0] = new StructWithPackedInlineArray { A = 5, B = 10 };
        data[0].Buffer[0] = 3;

        // SourceGenerator should emit this wrapper class and method.
        GEN_33_StructLayoutKernelsWrapper.LaunchModifyPackedInlineArrayKernel(
            data,
            blockDimX: 1
        );

        Assert.Equal(6, data[0].A);
        Assert.Equal(5, data[0].Buffer[0]);
        Assert.Equal(13, data[0].B);
    }
}
