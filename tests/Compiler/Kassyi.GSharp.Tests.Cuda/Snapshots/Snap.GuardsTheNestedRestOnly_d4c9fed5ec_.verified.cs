public class Lower_SyncGrid_ReturnInNestedBody_GuardsTheNestedRestOnly_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->c = 0;
            __gsharp_ctx->x += 1;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->__gsharp_sg_done_1 = false;
            if (__gsharp_ctx->c > 0)
            {
                __gsharp_ctx->__gsharp_sg_done_1 = true;
                goto __gsharp_sg_next_1_1;
            }

            goto case 2;
        }

        case 2:
            __gsharp_sg_next_1_1:
                ;
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_1)
            {
                __gsharp_ctx->x += 2;
            }

            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_ctx->x += 3;
            __gsharp_state = -1;
            return;
        }
    }
}
}