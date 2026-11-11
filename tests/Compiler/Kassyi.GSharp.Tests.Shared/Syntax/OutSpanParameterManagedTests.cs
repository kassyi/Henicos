#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Managed state-machine execution counterpart of <see cref="OutSpanParameterTests"/>.</summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class OutSpanParameterManagedTests : IDisposable
{
    private readonly bool _previousForceManaged;

    public OutSpanParameterManagedTests()
    {
        _previousForceManaged = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
    }

    public void Dispose()
    {
        CpuKernelLoader.ForceManagedExecution = _previousForceManaged;
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void OutArgument_OverwritesASpanParameterOnTheManagedPath_ReadsTheNewSpan() =>
        OutSpanParameterTests.AssertOutSpan();
}
#endif
