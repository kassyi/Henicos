using System.Collections.Generic;

public class TestContainer
{
    public void TestMethod(IEnumerable<int> items)
    {
        foreach (var item in items) { }
    }
}
