// ReSharper disable All
#nullable enable
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.DisposeHelperRelease;

// #786: fields that the Dispose method releases through a helper of the same type (GSH0022 must not report them), and
// the forms that still leave a field unreleased (GSH0022 reports them at the identifier of Dispose)
public sealed class Resource : IDisposable
{
    public void Dispose() { }
}

public static class Logger
{
    public static void Log(Resource resource) { }

    public static void Release(ref Resource? resource) { }
}

// No GSH0022: each field passed by ref to a helper that disposes its parameter, the errors thrown once at the end
public sealed class RefHelper : IDisposable
{
    private Resource? _a = new Resource();
    private Resource? _b = new Resource();

    public void Dispose()
    {
        List<Exception>? errors = null;
        Release(ref _a, ref errors);
        Release(ref this._b, ref errors);
        if (errors != null)
        {
            throw new AggregateException(errors);
        }
    }

    private static void Release(ref Resource? field, ref List<Exception>? errors)
    {
        try
        {
            field?.Dispose();
        }
        catch (Exception ex)
        {
            (errors ??= new List<Exception>()).Add(ex);
        }
        finally
        {
            field = null;
        }
    }
}

// No GSH0022: conditional access
public sealed class ConditionalAccess : IDisposable
{
    private Resource? _a = new Resource();

    public void Dispose() => _a?.Dispose();
}

// No GSH0022: a helper that passes its parameter on to the helper that disposes it (two levels), by name
public sealed class NestedHelper : IDisposable
{
    private Resource _a = new Resource();

    public void Dispose() => Outer(target: ref _a);

    private void Outer(ref Resource target) => Inner(ref target);

    private static void Inner(ref Resource value) => value.Dispose();
}

// No GSH0022: a local function of Dispose, a parameter passed by value, and a helper that disposes the field by name
public sealed class LocalAndValueHelpers : IDisposable
{
    private Resource _a = new Resource();
    private Resource _b = new Resource();
    private Resource _c = new Resource();

    public void Dispose()
    {
        Close(ref _a);
        DisposeValue(_b);
        ReleaseFields();

        static void Close(ref Resource r) => r.Dispose();
    }

    private static void DisposeValue(Resource r) => r.Dispose();

    private void ReleaseFields() => _c.Dispose();
}

// No GSH0022: passed to a method whose body the analysis cannot see (escaped)
public sealed class Escaped : IDisposable
{
    private Resource? _a = new Resource();

    public void Dispose() => Logger.Release(ref _a);
}

// No GSH0022: through a chain of helpers deeper than the analysis follows
public sealed class DeepChain : IDisposable
{
    private Resource _b = new Resource();

    public void Dispose() => Step1(ref _b);

    private static void Step1(ref Resource r) => Step2(ref r);

    private static void Step2(ref Resource r) => Step3(ref r);

    private static void Step3(ref Resource r) => Step4(ref r);

    private static void Step4(ref Resource r) => r.Dispose();
}

// GSH0022: the helper clears the field without disposing it
public sealed class HelperWithoutDispose : IDisposable
{
    private Resource? _a = new Resource();

    public void Dispose() => Clear(ref _a);

    private static void Clear(ref Resource? field) => field = null;
}

// GSH0022 for _b: only _a is passed to the helper
public sealed class OneFieldMissing : IDisposable
{
    private Resource _a = new Resource();
    private Resource _b = new Resource();

    public void Dispose()
    {
        Release(ref _a);
    }

    private static void Release(ref Resource r) => r.Dispose();
}

// GSH0022: two helpers that pass the field to each other and never dispose it
public sealed class HelperCycle : IDisposable
{
    private Resource _a = new Resource();
    private bool _first = true;

    public void Dispose() => Ping(ref _a);

    private void Ping(ref Resource r)
    {
        if (_first)
        {
            _first = false;
            Pong(ref r);
        }
    }

    private void Pong(ref Resource r) => Ping(ref r);
}

// GSH0027: a borrowed field released through a helper; logging another borrowed field (escaped) is not a release
public sealed class BorrowedThroughHelper : IDisposable
{
    private Resource _c;
    private Resource _d;

    public BorrowedThroughHelper(Resource c, Resource d)
    {
        _c = c;
        _d = d;
    }

    public void Dispose()
    {
        Release(ref _c);
        Logger.Log(_d);
    }

    private static void Release(ref Resource r) => r.Dispose();
}
