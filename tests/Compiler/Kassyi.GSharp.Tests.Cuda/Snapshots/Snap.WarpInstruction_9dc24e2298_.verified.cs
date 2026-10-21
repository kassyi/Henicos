public class Hoist_WarpInstruction_FromAssignment_Snapshot
{
    public void LoweredMethod()
{
    var __gsharp_warp_temp_0 = Gpu.WarpShuffle(y, 0);
    int a = x + __gsharp_warp_temp_0 * z;
}
}