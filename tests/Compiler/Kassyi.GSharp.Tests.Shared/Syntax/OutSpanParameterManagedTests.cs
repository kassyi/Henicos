#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterpart of <see cref="OutSpanParameterTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class OutSpanParameterManagedTests : IDisposable
{
    private readonly GpuExecutionScope _managedScope;

    public OutSpanParameterManagedTests()
    {
        _managedScope = GpuExecution.Use(KernelExecution.Managed);
    }

    public void Dispose()
    {
        _managedScope.Dispose();
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OutArgument_OverwritesASpanParameterOnTheManagedPath_ReadsTheNewSpan() =>
        OutSpanParameterTests.AssertOutSpan();
}
#endif
