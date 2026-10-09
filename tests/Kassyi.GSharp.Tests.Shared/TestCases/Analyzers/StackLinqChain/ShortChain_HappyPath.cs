using Kassyi.ZeroAlloc.Linq;

public class TestClass
{
    public void TestMethod()
    {
        var seq = StackEnumerable.Range(0, 10).Take(5).Skip(2);
    }
}
