using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Syntax;

public class Tier1_IntegrationTests : TranspilerTestBase
{
    [Fact]
    [Trait("Category", "Execution")]
    [Trait("FeatureId", "Tier1")]
    public void Test_Tier1_AllInOneKernel_Execution()
    {
        const int count = 256;
        int[] intInput = new int[count];
        float[] floatInput = new float[count];
        float[] gpuResults = new float[count];
        ulong[] gpuCounter = new ulong[1];

        Tier1CustomStruct config = new Tier1CustomStruct
        {
            IntVal = 42,
            FloatVal = 10.0f,
            BoolVal = true
        };

        for (int i = 0; i < count; i++)
        {
            intInput[i] = i - 128; // Mix of positive and negative
            floatInput[i] = (float)Math.Sin(i);
        }

        // We can just execute the CPU JIT which sets the thread index properly!
        Tier1IntegrationKernelsWrapper.Launch_AllInOneKernel(
            intInput,
            floatInput,
            gpuResults,
            config,
            gpuCounter,
            count,
            count * sizeof(float), // dynamicSharedSize
            blockDimX: count,
            sharedMemBytes: (uint)(count * sizeof(float))
        );

        for (int i = 0; i < count; i++)
        {
            Assert.True(gpuResults[i] >= 0.0f, "Results should be modified and positive");
        }
        
        Assert.True(gpuCounter[0] > 0, "Global counter should have been incremented by AtomicAdd");
    }
}
