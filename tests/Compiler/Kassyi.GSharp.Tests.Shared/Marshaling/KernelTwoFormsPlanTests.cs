#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Marshaling;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Unit tests for <see cref="KernelTwoFormsPlan"/> verifying plan computation for matching kernels
/// (which require both by-value and by-address forms on CUDA) and non-matching kernels.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelTwoFormsPlanTests
{
    private const string Source = @"
using Kassyi.GSharp.Core;

public static class TestKernels
{
    [GpuKernel]
    public static void MatchingKernel(in int first, ref int second)
    {
    }

    [GpuKernel]
    public static void NonMatchingKernel(int first, ref int second)
    {
    }

    [GpuKernel]
    public static void UnrelatedTypeInKernel(in int first, ref float second)
    {
    }
}
";

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_MatchingKernel_PlansTwoFormsWithByAddressSuffix()
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers("MatchingKernel").OfType<IMethodSymbol>().Single();

        var plan = KernelTwoFormsPlan.Create(method);

        Assert.True(plan.HasTwoForms);
        Assert.EndsWith("MatchingKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName + GSharpInternalNames.Kernel.InByAddressSuffix, plan.ByAddressKernelName);
        Assert.True(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_NonMatchingKernel_PlansSingleFormWithoutSuffix()
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers("NonMatchingKernel").OfType<IMethodSymbol>().Single();

        var plan = KernelTwoFormsPlan.Create(method);

        Assert.False(plan.HasTwoForms);
        Assert.EndsWith("NonMatchingKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName, plan.ByAddressKernelName);
        Assert.False(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_UnrelatedTypeInKernel_PlansSingleFormWithoutSuffix()
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers("UnrelatedTypeInKernel").OfType<IMethodSymbol>().Single();

        var plan = KernelTwoFormsPlan.Create(method);

        Assert.False(plan.HasTwoForms);
        Assert.EndsWith("UnrelatedTypeInKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName, plan.ByAddressKernelName);
        Assert.False(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PointerLaunchPlan_MatchingKernelInParameter_ClassifiesAsValueNotReference()
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers("MatchingKernel").OfType<IMethodSymbol>().Single();
        var inParam = method.Parameters[0];
        var shape = KernelParameterShape.Classify(inParam);

        Assert.True(shape.IsInReference);
        Assert.Equal(PointerArgumentKind.Value, PointerLaunchPlan.Classify(shape));
        var launcherParam = LauncherSignatures.GetParameter(shape, LauncherKind.Pointer);
        Assert.Equal("global::System.Int32", launcherParam.TypeName);
        Assert.Equal(string.Empty, launcherParam.Modifier);
    }
}
