public class Lower_WarpInstruction_InForIncrementor_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->i = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->i < limit))
                goto case 4;
            steps++;
            goto case 2;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_arg_temp_0 = __gsharp_ctx->i + 1;
            __gsharp_warp_exchange_assign(__gsharp_ctx->__gsharp_arg_temp_0);
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            var __gsharp_warp_temp_1 = __gsharp_warp_shuffle_read_sync(__gsharp_root, FullMask, __gsharp_ctx->__gsharp_arg_temp_0, 0, 32, "an unknown place");
            __gsharp_ctx->i = __gsharp_warp_temp_1;
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