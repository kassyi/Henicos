public class Lower_WarpInstruction_InWhileCondition_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_exchange_assign(count);
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_sync(__gsharp_root, FullMask, count, 0);
            if (!(__gsharp_warp_temp_0 < limit))
                goto case 4;
            count++;
            goto case 3;
        }

        case 3:
        {
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_state = -1;
            return;
        }
    }
}
}