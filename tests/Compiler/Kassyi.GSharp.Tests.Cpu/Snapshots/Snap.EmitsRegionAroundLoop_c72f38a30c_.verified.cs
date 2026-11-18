public class Lower_BackwardGotoLoop_EmitsRegionAroundLoop_Snapshot
{
    // t -> __gsharp_id_1_t
    // n -> __gsharp_id_2_n
    // m -> __gsharp_id_3_m
    // __gsharp_warp_temp_1 -> __gsharp_warp_temp_1
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->n = 0;
            __gsharp_ctx->m = 0u;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            goto case 1;
        }

        case 1:
        {
            ;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 1, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoop.cs(14,13)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoop.cs(14,13)");
            __gsharp_ctx->m = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_ctx->n++;
            if ((__gsharp_ctx->t & 1) == 1 && __gsharp_ctx->n < 2)
            {
                goto case 1;
            }

            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoop.cs(13,5)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_ctx->r[__gsharp_ctx->t] = __gsharp_ctx->m;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 2, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoop.cs(22,21)");
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceGotoLoop.cs(22,21)");
            __gsharp_ctx->r[__gsharp_ctx->t + 32] = __gsharp_warp_temp_0;
            __gsharp_state = -1;
            return;
        }
    }
}
}