public class Lower_SyncGrid_NestedReturnAfterReturn_GuardsWithBothFlags_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->c = 0;
            __gsharp_ctx->__gsharp_sg_done_0 = false;
            if (__gsharp_ctx->c > 0)
            {
                __gsharp_ctx->__gsharp_sg_done_0 = true;
                goto __gsharp_sg_next_0_0;
            }

            goto case 1;
        }

        case 1:
            __gsharp_sg_next_0_0:
                ;
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->__gsharp_sg_done_1 = false;
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                if (__gsharp_ctx->c > 1)
                {
                    __gsharp_ctx->__gsharp_sg_done_1 = true;
                    goto __gsharp_sg_next_1_1;
                }
            }

            goto case 3;
        }

        case 3:
            __gsharp_sg_next_1_1:
                ;
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_0 && !__gsharp_ctx->__gsharp_sg_done_1)
            {
                __gsharp_ctx->x += 1;
            }

            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                __gsharp_ctx->x += 2;
            }

            __gsharp_state = -1;
            return;
        }
    }
}
}