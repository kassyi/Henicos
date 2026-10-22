using System;

public class DummyDisposable : IDisposable
{
    public void Dispose() {}
}

public class MyWrapper
{
    public static implicit operator DummyDisposable(MyWrapper w) => new();
}
