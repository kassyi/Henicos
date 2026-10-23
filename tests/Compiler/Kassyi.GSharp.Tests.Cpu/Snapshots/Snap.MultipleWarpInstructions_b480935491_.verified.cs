public class Hoist_MultipleWarpInstructions_FromSameStatement_Snapshot
{
    public void LoweredMethod()
{
    var __gsharp_warp_temp_0 = Warp.ShuffleSync(mask, x, 1);
    var __gsharp_warp_temp_1 = Warp.ShuffleXorSync(mask, y, 1);
    var a = __gsharp_warp_temp_0 + __gsharp_warp_temp_1;
}
}