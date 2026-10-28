public class Lower_WarpInstruction_OnLeftOfAnd_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_warp_exchange_assign(value);
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_sync(__gsharp_root, FullMask, value, 1, 32, "an unknown place");
            bool ok = __gsharp_warp_temp_0 > 0 && flag;
            __gsharp_state = -1;
            return;
        }
    }
}
}