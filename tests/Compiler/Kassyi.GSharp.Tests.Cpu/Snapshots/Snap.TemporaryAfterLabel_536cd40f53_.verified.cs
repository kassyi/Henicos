public class Lower_LabeledShuffle_TemporaryAfterLabel_Snapshot
{
    // tid -> __gsharp_id_1_tid
    // v -> __gsharp_id_2_v
    // acc -> __gsharp_id_3_acc
    // k -> __gsharp_id_4_k
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->v = __gsharp_ctx->data[__gsharp_ctx->tid];
            __gsharp_ctx->acc = 0;
            __gsharp_ctx->k = 0;
            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_exchange_assign(__gsharp_ctx->v);
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_shuffle_read_down_sync(__gsharp_root, 0xFFFFFFFFu, __gsharp_ctx->v, 1, 32, "LabeledShuffle.cs(17,16)");
            __gsharp_ctx->acc += __gsharp_warp_temp_0;
            __gsharp_ctx->v += 1;
            if (++__gsharp_ctx->k < 3)
            {
                goto case 1;
            }

            __gsharp_ctx->data[__gsharp_ctx->tid] = __gsharp_ctx->acc;
            __gsharp_state = -1;
            return;
        }
    }
}
}