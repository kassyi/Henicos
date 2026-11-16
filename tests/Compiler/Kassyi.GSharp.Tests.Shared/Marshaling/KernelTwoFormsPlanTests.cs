#nullable enable
using System.Collections.Immutable;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules;
using Kassyi.GSharp.Compiler.Extensions;
using Kassyi.GSharp.Compiler.Helpers;
using Kassyi.GSharp.Compiler.Marshaling;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

/// <summary>
/// Unit tests for <see cref="KernelTwoFormsPlan"/> verifying plan computation from classified shapes
/// for matching kernels (which require both by-value and by-address forms on CUDA) and non-matching kernels.
/// </summary>
[Category(TestCategory.Compiler)]
public class KernelTwoFormsPlanTests
{
    private static readonly string Source = RoslynTestUtility.ReadTestCase("Marshaling/KernelTwoFormsPlanSource.cs");

    private static (KernelTwoFormsPlan Plan, IMethodSymbol Method) PlanFor(string methodName)
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var type = compilation.GetTypeByMetadataName("Kassyi.GSharp.Tests.Shared.TestCases.Marshaling.TestKernels");
        Assert.NotNull(type);
        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
        var shapes = method.Parameters
            .Select(KernelParameterShape.Classify)
            .ToImmutableArray()
            .AsEquatableArray();
        var kernelName = GlobalCppNames.GetKernelName(method);
        return (KernelTwoFormsPlan.Create(kernelName, shapes), method);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_MatchingKernel_PlansTwoFormsWithByAddressSuffix()
    {
        var (plan, _) = PlanFor("MatchingKernel");

        Assert.True(plan.HasTwoForms);
        Assert.EndsWith("MatchingKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName + GSharpInternalNames.Kernel.InByAddressSuffix, plan.ByAddressKernelName);
        Assert.Equal(GSharpInternalNames.Launcher.ByAddressFieldSuffix, plan.ByAddressFieldSuffix);
        Assert.Equal(plan.ByValueKernelName, plan.GetKernelName(Kassyi.GSharp.Compiler.Transpiler.KernelFormKind.ByValue));
        Assert.Equal(plan.ByAddressKernelName, plan.GetKernelName(Kassyi.GSharp.Compiler.Transpiler.KernelFormKind.ByAddress));
        Assert.True(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Create_NonMatchingKernel_PlansSingleFormWithoutSuffix()
    {
        var (plan, _) = PlanFor("NonMatchingKernel");

        Assert.False(plan.HasTwoForms);
        Assert.EndsWith("NonMatchingKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName, plan.ByAddressKernelName);
        Assert.Equal(plan.ByValueKernelName, plan.GetKernelName(Kassyi.GSharp.Compiler.Transpiler.KernelFormKind.ByValue));
        Assert.Equal(plan.ByValueKernelName, plan.GetKernelName(Kassyi.GSharp.Compiler.Transpiler.KernelFormKind.ByAddress));
        Assert.False(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Create_UnrelatedTypeInKernel_PlansSingleFormWithoutSuffix()
    {
        var (plan, _) = PlanFor("UnrelatedTypeInKernel");

        Assert.False(plan.HasTwoForms);
        Assert.EndsWith("UnrelatedTypeInKernel", plan.ByValueKernelName);
        Assert.Equal(plan.ByValueKernelName, plan.ByAddressKernelName);
        Assert.False(plan.IsPassedByAddress(0));
        Assert.False(plan.IsPassedByAddress(1));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void IsPassedByPointer_MatchingKernelInParameter_CountedAsPointerEightBytes()
    {
        var (_, method) = PlanFor("MatchingKernel");
        var inParam = method.Parameters[0];
        var refParam = method.Parameters[1];

        // inParam is passed by address (by pointer) when isKernel is true
        Assert.True(inParam.IsPassedByPointer(isKernel: true));
        Assert.True(refParam.IsPassedByPointer(isKernel: true));

        // When isKernel is false, in parameter of device function is also by pointer
        Assert.True(inParam.IsPassedByPointer(isKernel: false));

        // Non-matching in parameter is not passed by pointer on kernel
        var (_, nonMatchingMethod) = PlanFor("NonMatchingKernel");
        var valueParam = nonMatchingMethod.Parameters[0];
        Assert.False(valueParam.IsPassedByPointer(isKernel: true));

        var (_, unrelatedMethod) = PlanFor("UnrelatedTypeInKernel");
        var unrelatedInParam = unrelatedMethod.Parameters[0];
        Assert.False(unrelatedInParam.IsPassedByPointer(isKernel: true));
    }
}
