public class Lower_LoopLeftAfterContinue_EmitsBodyLeaveAndLoopTokenGate_Snapshot
{
    // t -> __gsharp_id_1_t
    // n -> __gsharp_id_2_n
    // __gsharp_warp_temp_1 -> __gsharp_warp_temp_1
    // __gsharp_warp_temp_2 -> __gsharp_warp_temp_2
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->n = 0;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            goto case 1;
        }

        case 1:
        {
            if (!(true))
                goto case 12;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceLoopLeftAfterContinue.cs(16,34)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceLoopLeftAfterContinue.cs(16,34)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * __gsharp_ctx->n] = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 3);
            if (!((__gsharp_ctx->t & 4) == 0))
                goto case 5;
            if ((__gsharp_ctx->t & 3) != 0 && __gsharp_ctx->n++ < 2)
            {
                __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 3);
                goto case 9;
            }

            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 3);
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceLoopLeftAfterContinue.cs(14,9)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 3);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 5, 0, 0, "Warp.ActiveMask", "ReconvergenceLoopLeftAfterContinue.cs(24,34)");
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_ctx->__gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 5, "ReconvergenceLoopLeftAfterContinue.cs(24,34)");
            __gsharp_ctx->result[__gsharp_ctx->t + 96] = __gsharp_ctx->__gsharp_warp_temp_2;
            goto case 7;
            goto case 5;
        }

        case 5:
        {
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 3);
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceLoopLeftAfterContinue.cs(14,9)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 3);
            goto case 7;
        }

        case 7:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceLoopLeftAfterContinue.cs(17,13)");
            __gsharp_state = 8;
            return;
        }

        case 8:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 3);
            goto case 13;
            goto case 9;
        }

        case 9:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceLoopLeftAfterContinue.cs(14,9)");
            __gsharp_state = 10;
            return;
        }

        case 10:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 2);
            goto case 11;
        }

        case 11:
        {
            goto case 1;
            goto case 12;
        }

        case 12:
        {
            goto case 13;
        }

        case 13:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceLoopLeftAfterContinue.cs(14,9)");
            __gsharp_state = 14;
            return;
        }

        case 14:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 6, 0, 0, "Warp.ActiveMask", "ReconvergenceLoopLeftAfterContinue.cs(30,27)");
            __gsharp_state = 15;
            return;
        }

        case 15:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 6, "ReconvergenceLoopLeftAfterContinue.cs(30,27)");
            __gsharp_ctx->result[__gsharp_ctx->t + 128] = __gsharp_warp_temp_0;
            __gsharp_ctx->result[__gsharp_ctx->t + 160] = (uint)__gsharp_ctx->n;
            __gsharp_state = -1;
            return;
        }
    }
}
}