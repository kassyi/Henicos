public class Hoist_WarpInstruction_InsideNestedLoops_Snapshot
{
    public void LoweredMethod()
{
    for (int outer = 0; outer < 2; outer++)
    {
        for (int offset = 1; offset < 32; offset <<= 1)
        {
            var __gsharp_warp_temp_0 = Warp.ShuffleSync(mask, value, offset + outer);
            value += __gsharp_warp_temp_0;
        }
    }
}
}