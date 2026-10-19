public class Hoist_NestedWarpInstructions_Snapshot
{
    public void LoweredMethod()
{
    var __gsharp_warp_temp_0 = Gpu.WarpShuffle(x, 1);
    var __gsharp_warp_temp_1 = Gpu.WarpShuffle(__gsharp_warp_temp_0, 2);
    var a = __gsharp_warp_temp_1;
}
}