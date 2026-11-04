public class Hoist_WarpInstruction_InsideNestedLoops_Snapshot
{
    public void LoweredMethod()
{
    for (int outer = 0; outer < 2; outer++)
    {
        for (int offset = 1; offset < 32; offset <<= 1)
        {
            var __gsharp_arg_temp_0 = offset + outer;
            var __gsharp_warp_temp_1 = Warp.ShuffleSync(mask, value, __gsharp_arg_temp_0);
            value += __gsharp_warp_temp_1;
        }
    }
}
}