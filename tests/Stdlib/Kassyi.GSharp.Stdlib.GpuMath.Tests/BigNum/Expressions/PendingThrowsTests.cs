#nullable enable

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Tests of the per-thread record of element exceptions that <c>Any</c> and <c>All</c> keep while they evaluate a chain
/// that can throw; no GPU is needed.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Unit)]
public class PendingThrowsTests
{
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void TryRecord_NoScope_ReturnsFalse()
    {
        // mutant: PendingThrows.TryRecord returning true without a scope
        Assert.False(PendingThrows.TryRecord(0, new DivideByZeroException()));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryRecord_OtherThread_DoesNotSeeScope()
    {
        using var scope = PendingThrows.Begin();
        var pending = scope.Instance;
        var recordedOnOtherThread = true;

        // mutant: PendingThrows.t_current without [ThreadStatic] (the other thread records into this scope)
        var thread = new Thread(() => recordedOnOtherThread = PendingThrows.TryRecord(0, new DivideByZeroException()));
        thread.Start();
        thread.Join();

        Assert.False(recordedOnOtherThread);
        Assert.Null(pending.Exception);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Begin_NestedScope_RestoresOuter()
    {
        var a = new DivideByZeroException();
        var b = new InvalidOperationException();
        var c = new OverflowException();

        using var outer = PendingThrows.Begin();
        Assert.True(PendingThrows.TryRecord(5, a));
        PendingThrows inner;
        using (var innerScope = PendingThrows.Begin())
        {
            inner = innerScope.Instance;
            Assert.True(PendingThrows.TryRecord(1, b));
        }

        // mutant: PendingThrows.Scope.Dispose setting t_current to null (the outer scope is lost)
        // mutant: PendingThrows.Scope.Dispose doing nothing (C goes to the inner scope)
        Assert.True(PendingThrows.TryRecord(3, c));

        Assert.Same(c, outer.Instance.Exception);
        Assert.Equal(3, outer.Instance.MinIndex);
        Assert.Same(b, inner.Exception);
        Assert.Equal(1, inner.MinIndex);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Suspend_HidesScopeAndRestores()
    {
        using var scope = PendingThrows.Begin();
        bool recordedWhileSuspended;
        using (PendingThrows.Suspend())
        {
            // mutant: PendingThrows.Suspend leaving t_current as it is
            recordedWhileSuspended = PendingThrows.TryRecord(0, new DivideByZeroException());
        }

        // mutant: PendingThrows.SuspendScope.Dispose leaving t_current null
        var recordedAfter = PendingThrows.TryRecord(0, new DivideByZeroException());

        Assert.False(recordedWhileSuspended);
        Assert.True(recordedAfter);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Record_LowerIndexThenEarlierOrder_Wins()
    {
        var x = new DivideByZeroException();
        var y = new InvalidOperationException();
        var z = new OverflowException();

        using var scope = PendingThrows.Begin();
        PendingThrows.TryRecord(3, x);
        PendingThrows.TryRecord(2, y);
        PendingThrows.TryRecord(2, z);

        // mutant: PendingThrows.Record without `index < _bestIndex` (X stays)
        // mutant: PendingThrows.Record `order < _bestOrder` -> `order > _bestOrder` (Z replaces Y)
        Assert.Same(y, scope.Instance.Exception);
        Assert.Equal(2, scope.Instance.MinIndex);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath | TestScenario.SadPath)]
    public void IsElementException_DomainAndOtherErrors_Separates()
    {
        // mutant: PendingThrows.IsElementException with `is InvalidOperationException` (ObjectDisposedException passes)
        Assert.True(PendingThrows.IsElementException(new DivideByZeroException()));
        Assert.True(PendingThrows.IsElementException(new OverflowException()));
        Assert.True(PendingThrows.IsElementException(new InvalidOperationException()));
        Assert.False(PendingThrows.IsElementException(new ObjectDisposedException("workspace")));
        Assert.False(PendingThrows.IsElementException(new ArgumentException()));
        Assert.False(PendingThrows.IsElementException(new ArgumentOutOfRangeException()));
    }
}
