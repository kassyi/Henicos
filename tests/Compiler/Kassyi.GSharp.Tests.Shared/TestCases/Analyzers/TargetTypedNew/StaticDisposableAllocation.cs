using System;

public class DummyDisposable : IDisposable
{
    public void Dispose() {}
}

public static class TargetTypedStaticContainer
{
    public static readonly DummyDisposable GlobalCtx = new();
}
