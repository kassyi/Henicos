public class Hoist_WarpInstruction_InsideSwitchSection_Snapshot
{
    public void LoweredMethod()
{
    switch (mode)
    {
        case 0:
        {
            var __gsharp_warp_temp_0 = Gpu.WarpShuffle(value, 1);
            value = __gsharp_warp_temp_0 + 1;
            break;
        }
    }
}
}