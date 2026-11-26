public class Lower_GotoLoopThroughForLoop_EmitsLatchAtBackwardGoto_Snapshot
{
    // t -> __gsharp_id_2_t
    // p -> __gsharp_id_3_p
    // __gsharp_warp_temp_0 -> __gsharp_warp_temp_0
    // i -> __gsharp_id_5_i
    // __gsharp_warp_temp_2 -> __gsharp_warp_temp_2
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->p = -1;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_ctx->p++;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopThroughLoop.cs(16,30)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceGotoLoopThroughLoop.cs(16,30)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * __gsharp_ctx->p] = __gsharp_ctx->__gsharp_warp_temp_0;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_ctx->i = 0;
            goto case 3;
        }

        case 3:
        {
            if (!(__gsharp_ctx->i < __gsharp_ctx->u + 2))
                goto case 8;
            if (!(__gsharp_ctx->p < 1 && (__gsharp_ctx->t & 3) == __gsharp_ctx->i + 1))
                goto case 5;
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoopThroughLoop.cs(14,5)");
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            goto case 1;
            goto case 5;
        }

        case 5:
        {
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopThroughLoop.cs(24,48)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_ctx->__gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceGotoLoopThroughLoop.cs(24,48)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * (2 + 2 * __gsharp_ctx->p + __gsharp_ctx->i)] = __gsharp_ctx->__gsharp_warp_temp_2;
            goto case 7;
        }

        case 7:
        {
            __gsharp_ctx->i++;
            goto case 3;
            goto case 8;
        }

        case 8:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceGotoLoopThroughLoop.cs(17,9)");
            __gsharp_state = 9;
            return;
        }

        case 9:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoopThroughLoop.cs(14,5)");
            __gsharp_state = 10;
            return;
        }

        case 10:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 5, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopThroughLoop.cs(27,27)");
            __gsharp_state = 11;
            return;
        }

        case 11:
        {
            var __gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 5, "ReconvergenceGotoLoopThroughLoop.cs(27,27)");
            __gsharp_ctx->result[__gsharp_ctx->t + 192] = __gsharp_warp_temp_1;
            __gsharp_ctx->result[__gsharp_ctx->t + 224] = (uint)__gsharp_ctx->p;
            __gsharp_state = -1;
            return;
        }
    }
}
}