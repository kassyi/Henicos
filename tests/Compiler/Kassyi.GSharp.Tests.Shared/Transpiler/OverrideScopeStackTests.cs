namespace Kassyi.GSharp.Tests.Shared.Transpiler;

/// <summary>
/// <see cref="OverrideScopeStack"/> resolves the name pushed last, restores the earlier entries on <c>Pop</c>,
/// enumerates in push order and grows past its initial capacity of 16.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class OverrideScopeStackTests
{
    private const int InitialCapacity = 16;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Push_PastInitialCapacity_KeepsEveryEntryInPushOrder()
    {
        var stack = new OverrideScopeStack();
        for (var i = 0; i <= InitialCapacity; i++)
        {
            Assert.Equal(i, stack.Push(Name(i), new OverrideValue(Value(i))));
        }

        Assert.Equal(InitialCapacity + 1, stack.Count);
        Assert.Equal(
            Enumerable.Range(0, InitialCapacity + 1).Select(static i => (Name(i), Value(i))),
            stack.Select(static item => (item.Name, item.Value.StringValue)));
        for (var i = 0; i <= InitialCapacity; i++)
        {
            Assert.True(stack.TryGet(Name(i), out var value), Name(i));
            Assert.Equal(Value(i), value.StringValue);
        }
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Pop_ToCookie_RemovesOnlyTheEntriesPushedAfterIt()
    {
        var stack = new OverrideScopeStack();
        for (var i = 0; i < InitialCapacity; i++)
        {
            stack.Push(Name(i), new OverrideValue(Value(i)));
        }

        stack.Pop(10);

        Assert.Equal(10, stack.Count);
        Assert.Equal(
            Enumerable.Range(0, 10).Select(static i => Name(i)),
            stack.Select(static item => item.Name));
        Assert.True(stack.TryGet(Name(9), out _));
        Assert.False(stack.TryGet(Name(10), out var removed));
        Assert.Null(removed.StringValue);

        // The freed slots are reused by the next push
        Assert.Equal(10, stack.Push("next", new OverrideValue("n")));
        Assert.Equal(11, stack.Count);
        Assert.False(stack.TryGet(Name(11), out _));
    }

    // At exactly the initial capacity the search starts at the last slot of the array
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void TryGet_AtExactlyTheInitialCapacity_FindsEveryEntry()
    {
        var stack = new OverrideScopeStack();
        for (var i = 0; i < InitialCapacity; i++)
        {
            stack.Push(Name(i), new OverrideValue(Value(i)));
        }

        for (var i = 0; i < InitialCapacity; i++)
        {
            Assert.True(stack.TryGet(Name(i), out var value), Name(i));
            Assert.Equal(Value(i), value.StringValue);
        }

        Assert.False(stack.TryGet("missing", out _));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void TryGet_SameNameTwice_FindsTheInnerOneUntilItIsPopped()
    {
        var stack = new OverrideScopeStack();
        stack.Push("x", new OverrideValue("outer"));
        var cookie = stack.Push("x", new OverrideValue("inner"));

        Assert.True(stack.TryGet("x", out var inner));
        Assert.Equal("inner", inner.StringValue);

        stack.Pop(cookie);

        Assert.True(stack.TryGet("x", out var outer));
        Assert.Equal("outer", outer.StringValue);
        Assert.False(stack.TryGet("y", out _));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Clear_AfterGrowing_LeavesAnEmptyStack()
    {
        var stack = new OverrideScopeStack();
        for (var i = 0; i <= InitialCapacity; i++)
        {
            stack.Push(Name(i), new OverrideValue(Value(i)));
        }

        stack.Clear();

        Assert.Equal(0, stack.Count);
        Assert.Empty(stack);
        Assert.False(stack.TryGet(Name(0), out _));
        Assert.Equal(0, stack.Push("a", new OverrideValue("b")));
    }

    private static string Name(int i) => "v" + i;

    private static string Value(int i) => "value" + i;
}
