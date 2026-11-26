public class Lower_GotoLoopEndingInSwitchSection_EmitsBreakGateAndJoinLabel_Snapshot
{
    // t -> __gsharp_id_2_t
    // n -> __gsharp_id_3_n
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
            goto case 1;
        }

        case 1:
        {
            ;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 2, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInSwitch.cs(15,30)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceGotoLoopInSwitch.cs(15,30)");
            __gsharp_ctx->result[__gsharp_ctx->t + 32 * __gsharp_ctx->n] = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            {
                var __gsharp_switch_temp_0 = (__gsharp_ctx->t & 4) + __gsharp_ctx->u;
                if (__gsharp_switch_temp_0 == 0)
                    goto case 3;
                goto case 6;
            }

            goto case 3;
        }

        case 3:
        {
            if ((__gsharp_ctx->t & 3) != 0 && __gsharp_ctx->n++ < 2)
            {
                __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 1);
                goto case 1;
            }

            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoopInSwitch.cs(14,5)");
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInSwitch.cs(24,34)");
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            __gsharp_ctx->__gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceGotoLoopInSwitch.cs(24,34)");
            __gsharp_ctx->result[__gsharp_ctx->t + 96] = __gsharp_ctx->__gsharp_warp_temp_2;
            goto case 9;
            goto case 6;
        }

        case 6:
        {
            __gsharp_warp_region_leave(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceGotoLoopInSwitch.cs(14,5)");
            __gsharp_state = 7;
            return;
        }

        case 7:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            goto case 9;
            goto case 8;
        }

        case 8:
        {
            goto case 9;
        }

        case 9:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceGotoLoopInSwitch.cs(16,9)");
            __gsharp_state = 10;
            return;
        }

        case 10:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceGotoLoopInSwitch.cs(30,27)");
            __gsharp_state = 11;
            return;
        }

        case 11:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceGotoLoopInSwitch.cs(30,27)");
            __gsharp_ctx->result[__gsharp_ctx->t + 128] = __gsharp_warp_temp_0;
            __gsharp_ctx->result[__gsharp_ctx->t + 160] = (uint)__gsharp_ctx->n;
            __gsharp_state = -1;
            return;
        }
    }
}
}