public class Hoist_WarpInstruction_InsideEmbeddedStatement_Snapshot
{
    public void LoweredMethod()
{
    for (int offset = 1; offset < 32; offset <<= 1)
    {
        var __gsharp_warp_temp_0 = Warp.ShuffleSync(mask, value, offset);
        value += __gsharp_warp_temp_0;
    }
}
}