namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A <c>using</c> in device code disposes its resource on every exit of its scope, in the order C# does. Each kernel
/// runs on one thread and writes its steps to a log (<see cref="UsingRecorder"/>): the bodies write 10 and up, the
/// code after a scope 20, and each <c>Dispose</c> the id of its resource.
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class UsingDisposalTests
{
    // The entries of the log, without the count in front
    private static int[] Run(Action<int[]> launch)
    {
        var log = new int[16];
        launch(log);
        return log[1..(log[0] + 1)];
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStatement_DisposesAtTheEndOfItsBody()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchUsingStatementKernel(log, blockDimX: 1));

        Assert.Equal([10, 1, 20], log);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, new[] { 10, 11, 1, 20 })]
    [InlineData(1, new[] { 10, 1 })]
    public void Test_UsingStatement_DisposesOnAnEarlyReturn(int stop, int[] expected)
    {
        var log = Run(log => UsingDisposalKernels.LaunchUsingEarlyReturnKernel(log, stop, blockDimX: 1));

        Assert.Equal(expected, log);
    }

    // i = 0 writes 0; i = 1 continues and i = 2 breaks out of the body, and both dispose
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UsingStatementInALoop_DisposesOnBreakAndContinue()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchUsingInLoopKernel(log, blockDimX: 1));

        Assert.Equal([0, 100, 101, 102, 20], log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_NestedUsings_DisposeInReverseOrder()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchNestedUsingKernel(log, blockDimX: 1));

        Assert.Equal([10, 2, 1, 11, 4, 3], log);
    }

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, new[] { 10, 11, 2, 1, 20 })]
    [InlineData(1, new[] { 10, 2, 1 })]
    public void Test_UsingDeclarations_DisposeAtTheEndOfTheBlockInReverseOrder(int stop, int[] expected)
    {
        var log = Run(log => UsingDisposalKernels.LaunchUsingDeclarationKernel(log, stop, blockDimX: 1));

        Assert.Equal(expected, log);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UsingDeclarationInALoop_DisposesInEachIteration()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchUsingDeclarationInLoopKernel(log, blockDimX: 1));

        Assert.Equal([0, 100, 101, 2, 102], log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefStructWithADisposeMethod_IsDisposed()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchPatternDisposeKernel(log, blockDimX: 1));

        Assert.Equal([10, 7, 11, 8], log);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingAnExpression_DisposesItsValue()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchUsingExpressionKernel(log, blockDimX: 1));

        Assert.Equal([10, 1], log);
    }

    // The return value is computed before the resource is disposed
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UsingInADeviceFunction_DisposesAfterTheReturnValue()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchDeviceFunctionUsingKernel(log, blockDimX: 1));

        Assert.Equal([10, 1, 11], log);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UsingBeforeABarrier_DisposesBeforeTheBarrier()
    {
        var log = Run(static log => UsingDisposalKernels.LaunchUsingBeforeBarrierKernel(log, blockDimX: 1));

        Assert.Equal([10, 1, 20], log);
    }
}
