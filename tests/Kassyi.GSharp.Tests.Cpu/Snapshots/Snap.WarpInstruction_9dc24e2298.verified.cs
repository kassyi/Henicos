public class Hoist_WarpInstruction_FromAssignment_Snapshot
{
    public void LoweredMethod()
{
    var _warpTemp_0 = Gpu.WarpShuffle(y, 0);
    int a = x + _warpTemp_0 * z;
}
}