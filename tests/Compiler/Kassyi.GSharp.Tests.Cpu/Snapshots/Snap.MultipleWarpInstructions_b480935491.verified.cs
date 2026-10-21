public class Hoist_MultipleWarpInstructions_FromSameStatement_Snapshot
{
    public void LoweredMethod()
{
    var _warpTemp_0 = Gpu.WarpShuffle(x, 1);
    var _warpTemp_1 = Gpu.WarpReduceSum(y);
    var a = _warpTemp_0 + _warpTemp_1;
}
}