public class Pipeline_DupDemo_DistinguishesHoistedFieldsAndLabelsPerRegion_Snapshot
{
    // tid -> __gsharp_id_1_tid
    // s -> __gsharp_il_1_2_s
    // s -> __gsharp_il_2_2_s
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->s = __gsharp_ctx->tid * 2;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            if (__gsharp_ctx->s > 4)
            {
                goto done;
            }

            output[__gsharp_ctx->tid] = __gsharp_ctx->s;
            done:
                ;
            __gsharp_ctx->s = __gsharp_ctx->tid * 2;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            if (__gsharp_ctx->s > 4)
            {
                goto done;
            }

            output[__gsharp_ctx->tid] = __gsharp_ctx->s;
            done:
                ;
            __gsharp_state = -1;
            return;
        }
    }
}
}