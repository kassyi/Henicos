using System;

public class DummyDisposable : IDisposable
{
    public void Dispose() {}
}

public class MultiAllocContainer
{
    private DummyDisposable _c1;
    private DummyDisposable _c2;

    public MultiAllocContainer()
    {
        _c1 = new();
        _c2 = new();
    }
}
