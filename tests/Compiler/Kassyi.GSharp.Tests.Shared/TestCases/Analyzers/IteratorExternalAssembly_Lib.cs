using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace ExternalLib;

public static class ExternalIterators
{
    [GpuDeviceFunction]
    public static IEnumerable<int> ExternalRange(int count)
    {
        yield return count;
    }
}
