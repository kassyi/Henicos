using System;

public class DummyDisposable : IDisposable
{
    public void Dispose() {}
}

public class PropertyGetterContainer
{
    public DummyDisposable Context => new();
}
