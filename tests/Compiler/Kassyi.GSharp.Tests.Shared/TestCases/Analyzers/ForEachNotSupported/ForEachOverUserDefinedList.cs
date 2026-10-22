using System;

namespace CustomNamespace
{
    public class List<T>
    {
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() => throw new NotImplementedException();
    }
}

public class TestContainer
{
    public void TestMethod(CustomNamespace.List<int> list)
    {
        foreach (var item in list) { }
    }
}
