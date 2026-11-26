public class Lower_GotoLoopWithTwoBackwardGotos_EmitsLatchAtEachGoto_Snapshot
{
    // t -> __gsharp_id_1_t
    // n -> __gsharp_id_2_n
    // p -> __gsharp_id_3_p
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
            __gsharp_ctx->p = -1;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_ctx->p++;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 2, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopLatch.cs(17,30)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceGotoLoopLatch.cs(17,30)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * __gsharp_ctx->p] = __gsharp_ctx->__gsharp_warp_temp_0;
            if (!((__gsharp_ctx->t & 3) == 1 && __gsharp_ctx->n++ < 1))
                goto case 4;
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoopLatch.cs(15,5)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopLatch.cs(23,36)");
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceGotoLoopLatch.cs(23,36)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * (2 + __gsharp_ctx->p)] = __gsharp_ctx->__gsharp_warp_temp_1;
            if (!((__gsharp_ctx->t & 3) == 2 && __gsharp_ctx->n++ < 1))
                goto case 7;
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoopLatch.cs(15,5)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            goto case 1;
            goto case 7;
        }

        case 7:
        {
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoopLatch.cs(15,5)");
            __gsharp_state = 8;
            return;
        }

        case 8:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopLatch.cs(29,27)");
            __gsharp_state = 9;
            return;
        }

        case 9:
        {
            var __gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceGotoLoopLatch.cs(29,27)");
            __gsharp_ctx->result[__gsharp_ctx->t + 128] = __gsharp_warp_temp_2;
            __gsharp_ctx->result[__gsharp_ctx->t + 160] = (uint)__gsharp_ctx->n;
            __gsharp_state = -1;
            return;
        }
    }
}
}