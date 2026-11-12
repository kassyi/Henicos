using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// The entry point of an application cannot be a kernel; in a library the same method is an ordinary kernel
public static partial class KernelDeclarationProgram
{
    [GpuKernel]
    public static void Main() { }
}
