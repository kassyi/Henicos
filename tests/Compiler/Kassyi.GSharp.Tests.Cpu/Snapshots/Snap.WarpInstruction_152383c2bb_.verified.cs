public class Hoist_WarpInstruction_InsideIfBlock_Snapshot
{
    public void LoweredMethod()
{
    if (lane > 0)
    {
        var __gsharp_warp_temp_0 = Gpu.WarpShuffle(value, lane);
        value = __gsharp_warp_temp_0 * 2;
    }
}
}