#nullable enable
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Checks that <see cref="HostArrayCopies"/> gives every holder of one array the same copy, gives a kept array no copy,
/// and reports a copy as new only once, so that the walk of an array its own elements hold ends.
/// </summary>
public class HostArrayCopiesTests
{
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public void Copy_TwoHoldersOfOneArray_GetOneCopy()
    {
        var copies = new HostArrayCopies();
        int[] original = [1, 2];
        int[]? first = original;
        int[]? second = original;

        var firstIsNew = copies.Copy(ref first);
        var secondIsNew = copies.Copy(ref second);

        Assert.Equal((true, false), (firstIsNew, secondIsNew));
        Assert.Same(first, second);
        Assert.NotSame(original, first);
        Assert.Equal(original, first);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Copy_KeptArray_StaysTheCallersArray()
    {
        var copies = new HostArrayCopies();
        int[] kept = [1];
        int[]? holder = kept;

        copies.Keep(kept);
        var isNew = copies.Copy(ref holder);

        Assert.False(isNew);
        Assert.Same(kept, holder);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void Copy_TheCopyItself_IsNotCopiedAgain()
    {
        var copies = new HostArrayCopies();
        int[]? holder = [3];
        copies.Copy(ref holder);
        var copy = holder;

        var isNew = copies.Copy(ref holder);

        Assert.False(isNew);
        Assert.Same(copy, holder);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Copy_NullArray_StaysNull()
    {
        var copies = new HostArrayCopies();
        int[]? holder = null;

        Assert.False(copies.Copy(ref holder));
        Assert.Null(holder);
    }
}
