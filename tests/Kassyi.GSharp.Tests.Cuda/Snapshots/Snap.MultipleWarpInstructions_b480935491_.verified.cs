public class Hoist_MultipleWarpInstructions_FromSameStatement_Snapshot
{
    public void LoweredMethod()
{
    var __gsharp_warp_temp_0 = Gpu.WarpShuffle(x, 1);
    var __gsharp_warp_temp_1 = Gpu.WarpReduceSum(y);
    var a = __gsharp_warp_temp_0 + __gsharp_warp_temp_1;
}
}