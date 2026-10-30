public class Pipeline_SwitchGotoCaseEnum_MatchesCaseByValue_Snapshot
{
    // tid -> __gsharp_id_2_tid
    // value -> __gsharp_id_3_value
    // mode -> __gsharp_id_4_mode
    // __gsharp_warp_temp_0 -> __gsharp_warp_temp_0
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->value = __gsharp_ctx->tid;
            __gsharp_ctx->mode = flag == 0 ? SwitchGotoCaseMode.First : SwitchGotoCaseMode.Second;
            {
                var __gsharp_switch_temp_0 = __gsharp_ctx->mode;
                if (__gsharp_switch_temp_0 == SwitchGotoCaseMode.First)
                    goto case 1;
                if (__gsharp_switch_temp_0 == SwitchGotoCaseMode.Second)
                    goto case 3;
                if (__gsharp_switch_temp_0 == SwitchGotoCaseMode.Third)
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
            __gsharp_ctx->__gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_xor_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->value, 1, 32, "SwitchGotoCaseEnum.cs(25,25)");
            __gsharp_ctx->value = __gsharp_ctx->__gsharp_warp_temp_0;
            goto case 4;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->value += 1000;
            goto case 6;
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
            var __gsharp_warp_temp_1 = __gsharp_warp_shuffle_read_xor_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->value, 2, 32, "SwitchGotoCaseEnum.cs(31,25)");
            __gsharp_ctx->value = __gsharp_warp_temp_1 + 100;
            goto case 6;
            goto case 6;
        }

        case 6:
        {
            output[__gsharp_ctx->tid] = __gsharp_ctx->value;
            __gsharp_state = -1;
            return;
        }
    }
}
}