using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Transpiler;

[SkipSnapshotConsistency]
public partial class MemoryExtensionsTranspilerRule_HappyPath
{
    [GpuKernel]
    public static void Kernel(Span<int> data)
    {
        // This will trigger MemoryExtensions.Sort which targets MemoryExtensionsTranspilerRule
        MemoryExtensions.Sort(data);
        data.Sort();
    }
}
