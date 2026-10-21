public class Lower_WarpInstruction_InDoWhileCondition_Snapshot
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
            count++;
            goto case 2;
        }

        case 2:
        {
            __gsharp_warp_exchange_assign(count);
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_sync(__gsharp_root, FullMask, count, 0);
            if (__gsharp_warp_temp_0 < limit)
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