using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Xunit;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

[TestTarget(ExecutionTarget.GpuExecution)]
public partial class WrapperAllocationTests : TranspilerTestBase
{
    [SkippableFact]
    [Category(TestCategory.Compiler)]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_ConstantParameterBelowAndAboveThreshold_PassesConstantAndUpdatesOutputs()
    {
        int[] arrayVal = [42];

        int ioBelow = 5;
        WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioBelow, out float outBelow, arrayVal, constantVal: 10);
        Assert.Equal(0f, outBelow);
        Assert.Equal(5, ioBelow);

        int ioAbove = 15;
        WrapperAllocationKernels.LaunchAllocationTestKernel(ref ioAbove, out float outAbove, arrayVal, constantVal: 10);
        Assert.Equal(42f, outAbove);
        Assert.Equal(43, ioAbove);
    }
}
