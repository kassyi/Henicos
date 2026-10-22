using System;

public class TestContainer
{
    public void TestMethod(int[] arr, Span<int> span)
    {
        foreach (var item in arr) { }
        foreach (var item in span) { }
    }
}
