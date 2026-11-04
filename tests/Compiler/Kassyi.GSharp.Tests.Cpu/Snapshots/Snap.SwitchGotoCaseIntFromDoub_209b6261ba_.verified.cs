public class Pipeline_SwitchGotoCaseIntFromDouble_ReachesConvertedCase_Snapshot
{
    // tid -> __gsharp_id_2_tid
    // value -> __gsharp_id_3_value
    // __gsharp_warp_temp_0 -> __gsharp_warp_temp_0
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->value = __gsharp_ctx->tid;
            {
                var __gsharp_switch_temp_0 = __gsharp_ctx->flag;
                if (__gsharp_switch_temp_0 == 0)
                    goto case 1;
                if (__gsharp_switch_temp_0 == 1)
                    goto case 3;
                if (__gsharp_switch_temp_0 == 2)
                    goto case 4;
                goto case 6;
            }

            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_exchange_assign(__gsharp_ctx->value);
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_xor_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->value, 1, 32, "SwitchGotoCaseIntFromDouble.cs(17,25)");
            __gsharp_ctx->value = __gsharp_ctx->__gsharp_warp_temp_0;
            goto case 3;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->value += 10;
            goto case 7;
            goto case 4;
        }

        case 4:
        {
            __gsharp_warp_exchange_assign(__gsharp_ctx->value);
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            var __gsharp_warp_temp_1 = __gsharp_warp_shuffle_read_xor_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->value, 2, 32, "SwitchGotoCaseIntFromDouble.cs(23,25)");
            __gsharp_ctx->value = __gsharp_warp_temp_1;
            goto case 3;
            goto case 6;
        }

        case 6:
        {
            __gsharp_ctx->value += 1000;
            goto case 7;
            goto case 7;
        }

        case 7:
        {
            __gsharp_ctx->output[__gsharp_ctx->tid] = __gsharp_ctx->value;
            __gsharp_state = -1;
            return;
        }
    }
}
}