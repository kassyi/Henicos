using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// Sad Path: This class is not partial, but it contains a [GpuKernel] method.
public class GSH36_KernelClassMustBePartial_SadPath
{
    [GpuKernel]
    public static void MissingPartialModifierKernel()
    {
    }
}
