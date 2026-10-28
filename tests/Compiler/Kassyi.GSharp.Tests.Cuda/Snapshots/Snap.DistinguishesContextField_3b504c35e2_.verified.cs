public class Managed_DupDemo_DistinguishesContextFieldsPerRegion_Snapshot
{
    // tid -> tid
    // s -> __gsharp_il_1_2_s
    // s -> __gsharp_il_2_2_s
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->__gsharp_il_1_2_s = __gsharp_ctx->tid * 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            if (__gsharp_ctx->__gsharp_il_1_2_s > 4)
            {
                goto __gsharp_label_1_done;
            }

            output[__gsharp_ctx->tid] = __gsharp_ctx->__gsharp_il_1_2_s;
            __gsharp_label_1_done:
                ;
            __gsharp_ctx->__gsharp_il_2_2_s = __gsharp_ctx->tid * 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            if (__gsharp_ctx->__gsharp_il_2_2_s > 4)
            {
                goto __gsharp_label_2_done;
            }

            output[__gsharp_ctx->tid] = __gsharp_ctx->__gsharp_il_2_2_s;
            __gsharp_label_2_done:
                ;
            __gsharp_state = -1;
            return;
        }
    }
}
}