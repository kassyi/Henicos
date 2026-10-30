using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// A local that one switch section declares and another uses is declared before the first case label in the C++, and its
// declaration becomes an assignment (#795). A span and a ref local cannot be declared first and assigned later.
public static partial class SwitchSectionLocalDiagnosticKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SharedSpanKernel(int[] a, int[] b, int flag)
    {
        switch (flag)
        {
            case 0:
                Span<int> span = a;
                span[0] = 1;
                break;
            default:
                span = b;
                span[0] = 2;
                break;
        }
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SharedRefLocalKernel(int[] a, int flag)
    {
        switch (flag)
        {
            case 0:
                ref int slot = ref a[0];
                slot = 1;
                break;
            default:
                slot = ref a[1];
                slot = 2;
                break;
        }
    }

    // Shared by no other section: each keeps its own declaration and no diagnostic
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void SectionOwnSpanKernel(int[] a, int flag)
    {
        switch (flag)
        {
            case 0:
                Span<int> span = a;
                span[0] = 1;
                break;
            default:
                a[1] = 2;
                break;
        }
    }
}
