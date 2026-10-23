using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

public class ContextInferenceFindingsClass
{
    // Reads the thread index directly; ThreadIndex is a top-level Core type, not a member of Gpu.
    public int ReadsThreadIndex() => ThreadIndex.X;

    // CallsFirst <-> CallsSecond is a cycle; only CallsFirst reaches Gpu.SyncGrid.
    public void CallsFirst()
    {
        CallsSecond();
        Gpu.SyncGrid();
    }

    public void CallsSecond()
    {
        CallsFirst();
    }
}
