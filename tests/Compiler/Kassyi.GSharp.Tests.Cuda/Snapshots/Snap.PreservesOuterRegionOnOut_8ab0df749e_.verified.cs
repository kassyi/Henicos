public class Pipeline_NestedDupDemo_PreservesOuterRegionOnOuterReferences_Snapshot
{
    // tid -> __gsharp_id_1_tid
    // outerLocal -> __gsharp_il_1_2_outerLocal
    // innerLocal -> __gsharp_il_3_3_innerLocal
    // innerLocal -> __gsharp_il_4_3_innerLocal
    // outerLocal -> __gsharp_il_2_2_outerLocal
    // innerLocal -> __gsharp_il_5_3_innerLocal
    // innerLocal -> __gsharp_il_6_3_innerLocal
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->outerLocal = __gsharp_ctx->tid + 1;
            __gsharp_ctx->innerLocal = __gsharp_ctx->outerLocal * 2;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            output[__gsharp_ctx->tid] = __gsharp_ctx->outerLocal + __gsharp_ctx->innerLocal;
            __gsharp_ctx->innerLocal = __gsharp_ctx->outerLocal * 2;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            output[__gsharp_ctx->tid] = __gsharp_ctx->outerLocal + __gsharp_ctx->innerLocal;
            __gsharp_ctx->outerLocal = __gsharp_ctx->tid + 1;
            __gsharp_ctx->innerLocal = __gsharp_ctx->outerLocal * 2;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            output[__gsharp_ctx->tid] = __gsharp_ctx->outerLocal + __gsharp_ctx->innerLocal;
            __gsharp_ctx->innerLocal = __gsharp_ctx->outerLocal * 2;
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            output[__gsharp_ctx->tid] = __gsharp_ctx->outerLocal + __gsharp_ctx->innerLocal;
            __gsharp_state = -1;
            return;
        }
    }
}
}