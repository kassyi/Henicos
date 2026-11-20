public class Lower_SwitchSecondEntryJoin_EmitsSwitchJoinRegion_Snapshot
{
    // t -> __gsharp_id_2_t
    // m1 -> __gsharp_id_3_m1
    // m2 -> __gsharp_id_4_m2
    // m3 -> __gsharp_id_5_m3
    // __gsharp_warp_temp_1 -> __gsharp_warp_temp_1
    // __gsharp_warp_temp_2 -> __gsharp_warp_temp_2
    // __gsharp_warp_temp_3 -> __gsharp_warp_temp_3
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->m1 = 0u;
            __gsharp_ctx->m2 = 0u;
            __gsharp_ctx->m3 = 0u;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            {
                var __gsharp_switch_temp_0 = (__gsharp_ctx->t & 1) + __gsharp_ctx->u;
                if (__gsharp_switch_temp_0 == 0)
                    goto case 1;
                if (__gsharp_switch_temp_0 == 1)
                    goto case 4;
                goto case 8;
            }

            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 2, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoin.cs(17,22)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceSwitchJoin.cs(17,22)");
            __gsharp_ctx->m1 = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoin.cs(18,22)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_ctx->__gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceSwitchJoin.cs(18,22)");
            __gsharp_ctx->m2 = __gsharp_ctx->__gsharp_warp_temp_2;
            goto case 5;
            goto case 4;
        }

        case 4:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            goto case 5;
        }

        case 5:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceSwitchJoin.cs(20,13)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoin.cs(21,22)");
            __gsharp_state = 7;
            return;
        }

        case 7:
        {
            __gsharp_ctx->__gsharp_warp_temp_3 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceSwitchJoin.cs(21,22)");
            __gsharp_ctx->m3 = __gsharp_ctx->__gsharp_warp_temp_3;
            goto case 8;
            goto case 8;
        }

        case 8:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceSwitchJoin.cs(14,9)");
            __gsharp_state = 9;
            return;
        }

        case 9:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_ctx->r[__gsharp_ctx->t] = __gsharp_ctx->m1;
            __gsharp_ctx->r[__gsharp_ctx->t + 32] = __gsharp_ctx->m2;
            __gsharp_ctx->r[__gsharp_ctx->t + 64] = __gsharp_ctx->m3;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 5, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoin.cs(28,21)");
            __gsharp_state = 10;
            return;
        }

        case 10:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 5, "ReconvergenceSwitchJoin.cs(28,21)");
            __gsharp_ctx->r[__gsharp_ctx->t + 96] = __gsharp_warp_temp_0;
            __gsharp_state = -1;
            return;
        }
    }
}
}