using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SuspendingCallArgumentTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void SuspendingCall_ManagedOmittedOptionalArguments_UsesDefaultValues() =>
        RunManaged(AssertOmittedOptionalArguments);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_ManagedNamedArguments_SkipsOptionalParameters() =>
        RunManaged(AssertNamedArgument);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void SuspendingCall_ManagedReorderedNamedArguments_MatchesArgumentsByName() =>
        RunManaged(AssertReorderedNamedArguments);

    private static void RunManaged(Action run)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            run();
        }
    }
}
