#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback of <see cref="LocalFunctionOrderTests"/>.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalFunctionOrderManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CallBeforeDeclaration_Managed_UsesTheLocalFunctionAboveItsDeclaration() =>
        LocalFunctionOrderScenarios.Run(s_runManaged, static data => LocalFunctionOrderKernels.LaunchCallBeforeDeclarationKernel(data, blockDimX: 4), x => x + 1);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CapturingCallBeforeDeclaration_Managed_CapturesTheLocalsAboveTheCall() =>
        LocalFunctionOrderScenarios.Run(s_runManaged, static data => LocalFunctionOrderKernels.LaunchCapturingCallBeforeDeclarationKernel(data, 3, blockDimX: 4), x => x * 6 + 3);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CallChainBeforeDeclaration_Managed_WritesTheUsedFunctionFirst() =>
        LocalFunctionOrderScenarios.Run(s_runManaged, static data => LocalFunctionOrderKernels.LaunchCallChainBeforeDeclarationKernel(data, blockDimX: 4), x => (x + 10) * 2);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void NestedCallBeforeDeclaration_Managed_UsesTheLocalFunctionInTheNestedBlock(int flag) =>
        LocalFunctionOrderScenarios.NestedCallBeforeDeclaration(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(7)]
    public void SelfRecursion_Managed_CallsItself(int bonus) =>
        LocalFunctionOrderScenarios.SelfRecursion(s_runManaged, bonus);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void MutualRecursion_Managed_CallsTheOtherFunction() =>
        LocalFunctionOrderScenarios.MutualRecursion(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void VoidRecursion_Managed_WritesThroughTheCapturedArray() =>
        LocalFunctionOrderScenarios.VoidRecursion(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecursionBeforeDeclaration_Managed_UsesTheGroupAboveItsDeclaration() =>
        LocalFunctionOrderScenarios.RecursionBeforeDeclaration(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void SwitchSectionCallBeforeDeclaration_Managed_UsesTheLocalFunctionOfTheSection(int mode) =>
        LocalFunctionOrderScenarios.SwitchSectionCallBeforeDeclaration(s_runManaged, mode);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UsingBodyCallBeforeDeclaration_Managed_UsesTheLocalFunctionInTheUsingBody() =>
        LocalFunctionOrderScenarios.UsingBodyCallBeforeDeclaration(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void DelegateOfRecursive_Managed_ConvertsTheLocalFunctionGroup() =>
        LocalFunctionOrderScenarios.DelegateOfRecursive(s_runManaged);


    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
