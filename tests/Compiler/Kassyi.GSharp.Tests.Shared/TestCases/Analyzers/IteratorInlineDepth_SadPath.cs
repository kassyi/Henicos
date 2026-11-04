using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public partial class IteratorInlineDepthSadPathKernels
{
    [GpuDeviceFunction]
    public static IEnumerable<int> Chain00()
    {
        foreach (var x in Chain01())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain01()
    {
        foreach (var x in Chain02())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain02()
    {
        foreach (var x in Chain03())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain03()
    {
        foreach (var x in Chain04())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain04()
    {
        foreach (var x in Chain05())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain05()
    {
        foreach (var x in Chain06())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain06()
    {
        foreach (var x in Chain07())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain07()
    {
        foreach (var x in Chain08())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain08()
    {
        foreach (var x in Chain09())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain09()
    {
        foreach (var x in Chain10())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain10()
    {
        foreach (var x in Chain11())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain11()
    {
        foreach (var x in Chain12())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain12()
    {
        foreach (var x in Chain13())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain13()
    {
        foreach (var x in Chain14())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain14()
    {
        foreach (var x in Chain15())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain15()
    {
        foreach (var x in Chain16())
        {
            yield return x;
        }
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain16()
    {
        foreach (var x in Chain17())
        {
            yield return x;
        }
        var s = "bad";
    }

    [GpuDeviceFunction]
    public static IEnumerable<int> Chain17()
    {
        yield return 42;
    }
}
