public class Lower_GotoLoopEndingInBlock_EmitsArriveInsideTheBlock_Snapshot
{
    // t -> __gsharp_id_1_t
    // n -> __gsharp_id_2_n
    // __gsharp_warp_temp_3 -> __gsharp_warp_temp_3
    // __gsharp_warp_temp_0 -> __gsharp_warp_temp_0
    // __gsharp_warp_temp_1 -> __gsharp_warp_temp_1
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->n = 0;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            goto case 1;
        }

        case 1:
        {
            ;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 1, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInBlock.cs(14,16)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_3 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoopInBlock.cs(14,16)");
            __gsharp_ctx->r[__gsharp_ctx->t] = __gsharp_ctx->__gsharp_warp_temp_3;
            __gsharp_ctx->n++;
            if ((__gsharp_ctx->t & 1) == 1 && __gsharp_ctx->n < 2)
            {
                goto case 1;
            }

            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoopInBlock.cs(13,5)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 2, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInBlock.cs(23,29)");
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_ctx->__gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceGotoLoopInBlock.cs(23,29)");
            __gsharp_ctx->r[__gsharp_ctx->t + 32] = __gsharp_ctx->__gsharp_warp_temp_0;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInBlock.cs(26,25)");
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceGotoLoopInBlock.cs(26,25)");
            __gsharp_ctx->r[__gsharp_ctx->t + 64] = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInBlock.cs(29,21)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            var __gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceGotoLoopInBlock.cs(29,21)");
            __gsharp_ctx->r[__gsharp_ctx->t + 96] = __gsharp_warp_temp_2;
            __gsharp_state = -1;
            return;
        }
    }
}
}