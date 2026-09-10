using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Core;

public class COR_01_StructLayoutTests
{
    [Fact]
    [Trait("Category", "GpuExecution")]
    [Trait("FeatureId", "COR-01")]
    public void Test_StructLayout_IsMaintained_InGpuKernel()
    {
        var data = new CustomLayoutStruct[1] 
        { 
            new CustomLayoutStruct { A = 5, B = 3.0f, C = 10.0 } 
        };

        // SourceGenerator should emit this wrapper class and method.
        COR_01_StructLayoutKernelsWrapper.Launch_ModifyStructLayoutKernel(
            data,
            blockDimX: 1
        );

        Assert.Equal(15, data[0].A);
        Assert.Equal(6.0f, data[0].B);
        Assert.Equal(9.0, data[0].C);
    }
}
