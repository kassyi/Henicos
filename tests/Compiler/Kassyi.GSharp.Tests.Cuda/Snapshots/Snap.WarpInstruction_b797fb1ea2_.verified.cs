public class Hoist_WarpInstruction_InsideLoopBody_Snapshot
{
    public void LoweredMethod()
{
    for (int offset = 1; offset < 32; offset <<= 1)
    {
        var __gsharp_warp_temp_0 = Gpu.WarpShuffle(value, offset);
        int neighbor = __gsharp_warp_temp_0 + 1;
        value += neighbor;
    }
}
}