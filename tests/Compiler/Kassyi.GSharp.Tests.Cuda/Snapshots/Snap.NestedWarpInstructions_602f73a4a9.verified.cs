public class Hoist_NestedWarpInstructions_Snapshot
{
    public void LoweredMethod()
{
    var _warpTemp_0 = Gpu.WarpShuffle(x, 1);
    var _warpTemp_1 = Gpu.WarpShuffle(_warpTemp_0, 2);
    var a = _warpTemp_1;
}
}