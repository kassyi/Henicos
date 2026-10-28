public class Managed_DupSpanDemo_DistinguishesRefStructLocalsPerRegion_Snapshot
{
    // view -> __gsharp_il_1_3_view
    // view -> __gsharp_il_2_3_view
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
            __gsharp_il_1_3_view = output;
            __gsharp_il_1_3_view[__gsharp_ctx->tid] = __gsharp_ctx->__gsharp_il_1_2_s;
            __gsharp_ctx->__gsharp_il_2_2_s = __gsharp_ctx->tid * 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_il_2_3_view = output;
            __gsharp_il_2_3_view[__gsharp_ctx->tid] = __gsharp_ctx->__gsharp_il_2_2_s;
            __gsharp_state = -1;
            return;
        }
    }
}
}