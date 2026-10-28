public class Lower_WarpInstruction_InNestedShortCircuit_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->__gsharp_cond_temp_0 = first;
            if (!(__gsharp_ctx->__gsharp_cond_temp_0))
                goto case 3;
            __gsharp_ctx->__gsharp_cond_temp_1 = second;
            if (!(!__gsharp_ctx->__gsharp_cond_temp_1))
                goto case 2;
            __gsharp_warp_exchange_assign(value);
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_sync(__gsharp_root, FullMask, value, 1, 32, "an unknown place");
            __gsharp_ctx->__gsharp_cond_temp_1 = __gsharp_warp_temp_0 > 0;
            goto case 2;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_cond_temp_0 = (__gsharp_ctx->__gsharp_cond_temp_1);
            goto case 3;
        }

        case 3:
        {
            bool ok = __gsharp_ctx->__gsharp_cond_temp_0;
            __gsharp_state = -1;
            return;
        }
    }
}
}