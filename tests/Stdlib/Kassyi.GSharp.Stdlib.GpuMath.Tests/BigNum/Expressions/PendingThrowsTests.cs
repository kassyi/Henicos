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

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void OpenSpace_NoScope_DoesNothing()
    {
        PendingThrows.OpenSpace();

        Assert.False(PendingThrows.IsActive);
        Assert.False(PendingThrows.TryRecord(0, new DivideByZeroException()));

        var closed = PendingThrows.CloseSpace();

        Assert.Equal(int.MaxValue, closed.Index);
        Assert.Null(closed.Exception);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CloseSpace_RecordsInside_ReturnsThemAndRestoresOuter()
    {
        var a = new DivideByZeroException();
        var b = new InvalidOperationException();

        using var scope = PendingThrows.Begin();
        Assert.True(PendingThrows.TryRecord(5, a));

        PendingThrows.OpenSpace();
        Assert.Equal(int.MaxValue, scope.Instance.MinIndex);
        Assert.Null(scope.Instance.Exception);
        Assert.True(PendingThrows.TryRecord(1, b));

        var closed = PendingThrows.CloseSpace();

        // mutant: PendingThrows.PopSpace not restoring _best from _outer
        Assert.Equal(1, closed.Index);
        Assert.Same(b, closed.Exception);
        Assert.Equal(5, scope.Instance.MinIndex);
        Assert.Same(a, scope.Instance.Exception);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void OpenSpace_Nested_KeepsEachLevel()
    {
        var ex0 = new DivideByZeroException();
        var ex1 = new InvalidOperationException();
        var ex1b = new ArgumentException();
        var ex2 = new OverflowException();

        using var scope = PendingThrows.Begin();
        Assert.True(PendingThrows.TryRecord(10, ex0));

        PendingThrows.OpenSpace();
        Assert.True(PendingThrows.TryRecord(4, ex1));

        PendingThrows.OpenSpace();
        Assert.True(PendingThrows.TryRecord(7, ex2));

        var inner2 = PendingThrows.CloseSpace();
        Assert.Equal(7, inner2.Index);
        Assert.Same(ex2, inner2.Exception);
        Assert.Equal(4, scope.Instance.MinIndex);
        Assert.Same(ex1, scope.Instance.Exception);

        Assert.True(PendingThrows.TryRecord(2, ex1b));

        var inner1 = PendingThrows.CloseSpace();
        Assert.Equal(2, inner1.Index);
        Assert.Same(ex1b, inner1.Exception);
        Assert.Equal(10, scope.Instance.MinIndex);
        Assert.Same(ex0, scope.Instance.Exception);

        PendingThrows.OpenSpace();
        var empty = PendingThrows.CloseSpace();
        Assert.Equal(int.MaxValue, empty.Index);
        Assert.Null(empty.Exception);
        Assert.Equal(10, scope.Instance.MinIndex);
        Assert.Same(ex0, scope.Instance.Exception);

        const int DeepNesting = 20;
        var deepExceptions = new Exception[DeepNesting];
        for (var i = 0; i < DeepNesting; i++)
        {
            PendingThrows.OpenSpace();
            deepExceptions[i] = new ArithmeticException($"depth-{i}");
            Assert.True(PendingThrows.TryRecord(100 + i, deepExceptions[i]));
        }

        for (var i = DeepNesting - 1; i >= 0; i--)
        {
            var popped = PendingThrows.CloseSpace();
            Assert.Equal(100 + i, popped.Index);
            Assert.Same(deepExceptions[i], popped.Exception);
        }

        Assert.Equal(10, scope.Instance.MinIndex);
        Assert.Same(ex0, scope.Instance.Exception);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecordedOnInterrupt_OpenSpace_ReturnsFirstInTime()
    {
        var a = new DivideByZeroException();
        var b = new InvalidOperationException();

        using var scope = PendingThrows.Begin();
        PendingThrows.OpenSpace();
        PendingThrows.TryRecord(7, a);
        PendingThrows.TryRecord(2, b);

        // mutant: PendingThrows.RecordedOnInterrupt returning _best.Exception while a space is open
        Assert.Same(a, scope.Instance.RecordedOnInterrupt);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecordedOnInterrupt_AllClosed_ReturnsBest()
    {
        var a = new DivideByZeroException();
        var b = new InvalidOperationException();
        var c = new OverflowException();

        using var scope = PendingThrows.Begin();
        PendingThrows.TryRecord(7, a);
        PendingThrows.TryRecord(2, b);

        // mutant: PendingThrows.RecordedOnInterrupt returning _first when all spaces are closed
        Assert.Same(b, scope.Instance.RecordedOnInterrupt);

        PendingThrows.OpenSpace();
        PendingThrows.TryRecord(1, c);
        var closed = PendingThrows.CloseSpace();

        Assert.Same(c, closed.Exception);
        Assert.Same(b, scope.Instance.RecordedOnInterrupt);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void RecordedOnInterrupt_ClosedSpaceDiscardedThenReopened_DoesNotSurfaceDiscarded()
    {
        var a = new DivideByZeroException();
        var b = new InvalidOperationException();
        var c = new OverflowException();
        var d = new ArithmeticException();

        using var scope = PendingThrows.Begin();
        PendingThrows.OpenSpace();
        PendingThrows.TryRecord(1, a);
        PendingThrows.CloseSpace();

        // mutant: PendingThrows._first stored scope-wide instead of per space frame
        PendingThrows.OpenSpace();
        Assert.Null(scope.Instance.RecordedOnInterrupt);

        PendingThrows.TryRecord(2, b);
        Assert.Same(b, scope.Instance.RecordedOnInterrupt);
        PendingThrows.CloseSpace();

        PendingThrows.TryRecord(5, c);
        PendingThrows.OpenSpace();
        Assert.Same(c, scope.Instance.RecordedOnInterrupt);

        PendingThrows.OpenSpace();
        PendingThrows.TryRecord(1, d);
        Assert.Same(c, scope.Instance.RecordedOnInterrupt);

        PendingThrows.CloseSpace();
        PendingThrows.CloseSpace();
        Assert.Same(c, scope.Instance.RecordedOnInterrupt);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CloseSpace_AtZeroDepthInActiveScope_ReturnsEmpty()
    {
        using var scope = PendingThrows.Begin();
        PendingThrows.OpenSpace();
        PendingThrows.CloseSpace();

        var extra = PendingThrows.CloseSpace();

        Assert.Equal(int.MaxValue, extra.Index);
        Assert.Null(extra.Exception);
    }
}
