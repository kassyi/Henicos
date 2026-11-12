using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

// Compiled as a generated file (*.g.cs): the kernels another generator writes are checked like the user's own
public partial struct GeneratedFileStruct
{
    [GpuKernel]
    public static void InStruct(int[] o) { o[0] = 42; }
}

public static partial class GeneratedFileKernels
{
    [GpuKernel]
    public static void ObjectParameter(object value, int[] o) { o[0] = 42; }
}
