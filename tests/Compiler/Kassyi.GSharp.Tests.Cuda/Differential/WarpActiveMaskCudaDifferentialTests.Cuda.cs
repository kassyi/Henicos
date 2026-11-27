#nullable enable
using System;
using Kassyi.GSharp.Tests.Shared.Differential;
using Xunit;

namespace Kassyi.GSharp.Tests.Cuda.Differential;

[Category(TestCategory.Differential)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class WarpActiveMaskCudaDifferentialTests
{
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ForVariantTripUnrollFour_RunsWithinClassicBounds()
    {
        CudaCapability.EnsureGpuAvailable();

        const int u = 0;
        var output = new uint[17 * 32];
        WarpActiveMaskCudaDifferentialKernels.LaunchForVariantTripUnrollFourKernel(output, u, blockDimX: 32);

        for (int iter = 0; iter < 15; iter++)
        {
            uint classicMask = 0u;
            for (int lane = 0; lane < 32; lane++)
            {
                if (iter < u + 1 + (lane & 7) * 2)
                {
                    classicMask |= 1u << lane;
                }
            }

            for (int lane = 0; lane < 32; lane++)
            {
                uint actual = output[lane + 32 * iter];
                bool isActive = iter < u + 1 + (lane & 7) * 2;
                if (!isActive)
                {
                    Assert.Equal(0u, actual);
                }
                else
                {
                    uint own = 1u << lane;
                    Assert.True(
                        (actual & own) != 0u && (actual & ~classicMask) == 0u,
                        $"iter {iter}, lane {lane}: got 0x{actual:X8}, expected subset of 0x{classicMask:X8} containing lane {lane}");
                }
            }
        }

        for (int lane = 0; lane < 32; lane++)
        {
            Assert.Equal(0u, output[lane + 15 * 32]);
            Assert.Equal(0xFFFFFFFFu, output[lane + 16 * 32]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_ConditionalAccessInUniformLoop_KeysByLoopVariable()
    {
        CudaCapability.EnsureGpuAvailable();

        var output = new uint[64];
        WarpActiveMaskCudaDifferentialKernels.LaunchConditionalAccessInUniformLoopKernel(output, blockDimX: 32);

        for (int lane = 0; lane < 32; lane++)
        {
            uint expected0 = (lane & 1) == 0 ? 0x55555555u : 0u;
            uint expected1 = (lane & 2) == 0 ? 0x33333333u : 0u;
            Assert.Equal(expected0, output[lane]);
            Assert.Equal(expected1, output[lane + 32]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_SwitchGotoCaseCrossingUsingAndFixed_SeparatesPassesOnGpu()
    {
        CudaCapability.EnsureGpuAvailable();

        var usingOutput = new uint[128];
        WarpActiveMaskCudaDifferentialKernels.LaunchSwitchGotoCaseCrossingUsingAndFixedKernel(
            usingOutput, 0, blockDimX: 32);

        for (int lane = 0; lane < 32; lane++)
        {
            bool isEven = (lane & 1) == 0;
            Assert.Equal(0xFFFFFFFFu, usingOutput[lane]);
            Assert.Equal(isEven ? 0x55555555u : 0u, usingOutput[lane + 32]);
            uint postGotoExpected = isEven ? 0x55555555u : 0xAAAAAAAAu;
            Assert.Equal(postGotoExpected, usingOutput[lane + 64]);
            Assert.Equal(postGotoExpected, usingOutput[lane + 96]);
        }

        var fixedOutput = new uint[96];
        WarpActiveMaskCudaDifferentialKernels.LaunchSwitchGotoCaseCrossingUsingAndFixedKernel(
            fixedOutput, 1, blockDimX: 32);

        for (int lane = 0; lane < 32; lane++)
        {
            bool isLowPair = (lane & 2) == 0;
            Assert.Equal(0xFFFFFFFFu, fixedOutput[lane]);
            Assert.Equal(isLowPair ? 0x33333333u : 0u, fixedOutput[lane + 32]);
            Assert.Equal(isLowPair ? 0x33333333u : 0xCCCCCCCCu, fixedOutput[lane + 64]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_BreakBlockConditionalAccess_SeparatesIterationsOnGpu()
    {
        CudaCapability.EnsureGpuAvailable();

        var output = new uint[160];
        WarpActiveMaskCudaDifferentialKernels.LaunchBreakBlockConditionalAccessKernel(output, blockDimX: 32);

        uint[] slotMasks = [0xFFFFFFFFu, 0xEEEEEEEEu, 0xCCCCCCCCu, 0x88888888u];
        for (int lane = 0; lane < 32; lane++)
        {
            int mod4 = lane & 3;
            for (int iter = 0; iter < 4; iter++)
            {
                uint expectedHeader = mod4 >= iter ? slotMasks[iter] : 0u;
                Assert.Equal(expectedHeader, output[lane + 32 * iter]);
            }

            Assert.Equal(0x11111111u << mod4, output[lane + 128]);
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ActiveMask_DivergentForConditionalAccess_KeysByTripCounterOnGpu()
    {
        CudaCapability.EnsureGpuAvailable();

        var output = new uint[64];
        WarpActiveMaskCudaDifferentialKernels.LaunchDivergentForConditionalAccessKernel(output, blockDimX: 32);

        for (int lane = 0; lane < 32; lane++)
        {
            bool isEven = (lane & 1) == 0;
            Assert.Equal(isEven ? 0xFFFFFFFFu : 0u, output[lane]);
            Assert.Equal(isEven ? 0x55555555u : 0xFFFFFFFFu, output[lane + 32]);
        }
    }
}
