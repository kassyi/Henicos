public class Pipeline_SwitchGotoCaseFloatRounded_ReachesRoundedCase_Snapshot
{
    // tid -> __gsharp_id_2_tid
    // value -> __gsharp_id_3_value
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->value = __gsharp_ctx->tid;
            {
                var __gsharp_switch_temp_0 = __gsharp_ctx->selector;
                if (__gsharp_switch_temp_0 == 0.1f)
                    goto case 1;
                if (__gsharp_switch_temp_0 == 0.2f)
                    goto case 3;
                goto case 4;
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
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_xor_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->value, 1, 32, "SwitchGotoCaseFloatRounded.cs(17,25)");
            __gsharp_ctx->value = __gsharp_warp_temp_0;
            goto case 5;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->value += 100;
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_ctx->value += 1000;
            goto case 5;
            goto case 5;
        }

        case 5:
        {
            __gsharp_ctx->output[__gsharp_ctx->tid] = __gsharp_ctx->value;
            __gsharp_state = -1;
            return;
        }
    }
}
}