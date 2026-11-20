public class Lower_SwitchSecondEntryChain_EmitsOrderedSwitchJoins_Snapshot
{
    // t -> __gsharp_id_2_t
    // m0a -> __gsharp_id_3_m0a
    // m0b -> __gsharp_id_4_m0b
    // m1 -> __gsharp_id_5_m1
    // m2 -> __gsharp_id_6_m2
    // __gsharp_warp_temp_1 -> __gsharp_warp_temp_1
    // __gsharp_warp_temp_2 -> __gsharp_warp_temp_2
    // __gsharp_warp_temp_3 -> __gsharp_warp_temp_3
    // __gsharp_warp_temp_4 -> __gsharp_warp_temp_4
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->m0a = 0u;
            __gsharp_ctx->m0b = 0u;
            __gsharp_ctx->m1 = 0u;
            __gsharp_ctx->m2 = 0u;
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 0);
            {
                var __gsharp_switch_temp_0 = (__gsharp_ctx->t % 3) + __gsharp_ctx->u;
                if (__gsharp_switch_temp_0 == 0)
                    goto case 1;
                if (__gsharp_switch_temp_0 == 1)
                    goto case 4;
                if (__gsharp_switch_temp_0 == 2)
                    goto case 8;
                goto case 12;
            }

            goto case 1;
        }

        case 1:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 3, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoinChain.cs(18,23)");
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_warp_temp_1 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 3, "ReconvergenceSwitchJoinChain.cs(18,23)");
            __gsharp_ctx->m0a = __gsharp_ctx->__gsharp_warp_temp_1;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 4, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoinChain.cs(19,23)");
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_ctx->__gsharp_warp_temp_2 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 4, "ReconvergenceSwitchJoinChain.cs(19,23)");
            __gsharp_ctx->m0b = __gsharp_ctx->__gsharp_warp_temp_2;
            goto case 5;
            goto case 4;
        }

        case 4:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 2);
            goto case 5;
        }

        case 5:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 2, "ReconvergenceSwitchJoinChain.cs(21,13)");
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 2);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 5, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoinChain.cs(22,22)");
            __gsharp_state = 7;
            return;
        }

        case 7:
        {
            __gsharp_ctx->__gsharp_warp_temp_3 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 5, "ReconvergenceSwitchJoinChain.cs(22,22)");
            __gsharp_ctx->m1 = __gsharp_ctx->__gsharp_warp_temp_3;
            goto case 9;
            goto case 8;
        }

        case 8:
        {
            __gsharp_warp_region_enter(__gsharp_root, __gsharp_ctx, 1);
            goto case 9;
        }

        case 9:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 1, "ReconvergenceSwitchJoinChain.cs(24,13)");
            __gsharp_state = 10;
            return;
        }

        case 10:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 1);
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 6, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoinChain.cs(25,22)");
            __gsharp_state = 11;
            return;
        }

        case 11:
        {
            __gsharp_ctx->__gsharp_warp_temp_4 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 6, "ReconvergenceSwitchJoinChain.cs(25,22)");
            __gsharp_ctx->m2 = __gsharp_ctx->__gsharp_warp_temp_4;
            goto case 12;
            goto case 12;
        }

        case 12:
        {
            __gsharp_warp_reconverge_arrive(__gsharp_root, __gsharp_ctx, 0, "ReconvergenceSwitchJoinChain.cs(15,9)");
            __gsharp_state = 13;
            return;
        }

        case 13:
        {
            __gsharp_warp_reconverge_pass(__gsharp_root, __gsharp_ctx, 0);
            __gsharp_ctx->r[__gsharp_ctx->t] = __gsharp_ctx->m0a;
            __gsharp_ctx->r[__gsharp_ctx->t + 32] = __gsharp_ctx->m0b;
            __gsharp_ctx->r[__gsharp_ctx->t + 64] = __gsharp_ctx->m1;
            __gsharp_ctx->r[__gsharp_ctx->t + 96] = __gsharp_ctx->m2;
            __gsharp_warp_publish(__gsharp_root, __gsharp_ctx, 7, 0, 0, "Warp.ActiveMask", "ReconvergenceSwitchJoinChain.cs(33,22)");
            __gsharp_state = 14;
            return;
        }

        case 14:
        {
            var __gsharp_warp_temp_0 = __gsharp_warp_active_mask_read(__gsharp_root, __gsharp_ctx, 7, "ReconvergenceSwitchJoinChain.cs(33,22)");
            __gsharp_ctx->r[__gsharp_ctx->t + 128] = __gsharp_warp_temp_0;
            __gsharp_state = -1;
            return;
        }
    }
}
}