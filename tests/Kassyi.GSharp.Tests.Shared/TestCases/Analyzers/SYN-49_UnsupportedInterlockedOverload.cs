using System.Threading;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN_49_UnsupportedInterlockedOverload
{
    [GpuKernel]
    public static void KernelWithUnsupportedInterlocked(object[] refs, object val)
    {
        Interlocked.Exchange(ref refs[0], val);
    }
}
