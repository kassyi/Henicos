#nullable enable
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Transpiler.Managed;

[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class ManagedStackEligibilityTests
{
    private static readonly Compilation s_compilation = RoslynTestUtility.CreateCompilation(
        RoslynTestUtility.ReadTestCase("Analysis/ManagedStackEligibilityCases.cs")
    );

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("EligibilityKernels", "BlockBarrierOnly", true)]
    [InlineData("EligibilityKernels", "SpanParameterWithBarrier", true)]
    [InlineData("EligibilityKernels", "CallsStackEligibleDeviceFunction", true)]
    [InlineData("EligibilityKernels", "WithWarpShuffle", false)]
    [InlineData("EligibilityKernels", "WithActiveMask", false)]
    [InlineData("EligibilityKernels", "CooperativeKernel", false)]
    [InlineData("EligibilityKernels", "WithoutBarrier", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceKernelWithBarrier", false)]
    [InlineData("EligibilityKernels", "NonSpanRefStructParameter", false)]
    [InlineData("EligibilityKernels", "CallsShuffleReachingDeviceFunction", false)]
    [InlineData("InstanceEligibilityTarget", "CallsInstanceDeviceFunction", false)]
    public void IsStackKernel_VariousShapes_MatchesExpectedEligibility(string typeName, string methodName,
        bool expected)
    {
        var method = GetMethod(typeName, methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackKernel(method, s_compilation));
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData("EligibilityKernels", "StackDeviceWait", true)]
    [InlineData("EligibilityKernels", "ShuffleDeviceWait", false)]
    [InlineData("InstanceEligibilityTarget", "InstanceDeviceWait", false)]
    public void IsStackFunction_VariousShapes_MatchesExpectedEligibility(string typeName, string methodName,
        bool expected)
    {
        var method = GetMethod(typeName, methodName);
        Assert.Equal(expected, ManagedStackEligibility.IsStackFunction(method, s_compilation));
    }

    private static IMethodSymbol GetMethod(string typeName, string methodName)
    {
        var type = s_compilation.GetTypeByMetadataName($"ManagedStackEligibilityCases.{typeName}") ??
            throw new InvalidOperationException($"Type '{typeName}' not found.");
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Single();
    }
}
