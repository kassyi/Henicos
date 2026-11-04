using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static partial class KernelTypeParameterCases
{
    // A kernel parameter with an unmanaged type parameter
    [GpuKernel]
    public static void KernelWithTypeParameter<T>(T x) where T : unmanaged
    {
    }
}
