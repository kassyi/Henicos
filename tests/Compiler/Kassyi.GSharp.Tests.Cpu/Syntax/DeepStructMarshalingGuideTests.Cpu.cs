using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class DeepStructMarshalingGuideTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void FermatMegaKernel_ManagedExecution_WritesEveryArrayField() => RunManaged(RunFermatMegaKernel);

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void ModernKernel_ManagedExecution_PassesThePinnedFieldsAsPointers() => RunManaged(RunModernKernel);

    private static void RunManaged(Action run)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            run();
        }
    }
}
