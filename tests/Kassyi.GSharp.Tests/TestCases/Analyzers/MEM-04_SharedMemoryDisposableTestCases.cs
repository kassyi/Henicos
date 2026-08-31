using System;
using Kassyi.GSharp.Core;

namespace OtherNamespace
{
    public struct SharedMemory<T>
    {
        public SharedMemory(int size) { }
    }
}

public struct DummyDisposable : IDisposable
{
    public void Dispose() { }
}

public struct DummyExplicitDisposable : IDisposable
{
    void IDisposable.Dispose() { }
}

public struct DummyCustomDispose
{
    public void Dispose() { }
}

public struct DummyValid
{
    public int Value;
}

public struct DummyDisposeBool
{
    public void Dispose(bool disposing) { }
}

class Class_MEM_04_SharedMemoryForbiddenDisposableRuleTests
{
    public void TestDisposable()
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<DummyDisposable>(64);
    }

    public void TestExplicitDisposable()
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<DummyExplicitDisposable>(64);
    }

    public void TestCustomDispose()
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<DummyCustomDispose>(64);
    }

    public void TestTargetTypedDisposable()
    {
        Kassyi.GSharp.Core.SharedMemory<DummyDisposable> mem = new(64);
    }

    public void TestValid()
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<DummyValid>(64);
    }

    public void TestDisposeBool()
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<DummyDisposeBool>(64);
    }

    public void TestUnrelatedNamespace()
    {
        var mem = new OtherNamespace.SharedMemory<DummyDisposable>(64);
    }

    public void TestGenericConstraint<T>() where T : unmanaged, IDisposable
    {
        var mem = new Kassyi.GSharp.Core.SharedMemory<T>(64);
    }
}
