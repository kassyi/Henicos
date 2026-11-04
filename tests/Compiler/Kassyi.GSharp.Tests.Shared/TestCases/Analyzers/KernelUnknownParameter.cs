using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static partial class KernelUnknownParameterCases
{
    // A kernel parameter with an unresolved/unknown type (CS0246)
    [GpuKernel]
    public static void KernelWithUnknownType(UnknownType x)
    {
    }
}
