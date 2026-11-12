using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// A file-local class cannot be reopened from the generated file, and two files may hold one of the same name (GSH0111)
file static partial class FileLocalKernels
{
    [GpuKernel]
    public static void InFileB(int[] o) { o[0] = 42; }
}
