public class Lower_SyncGrid_DeclarationsAfterReturn_MoveToTheTop_Snapshot
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
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                __gsharp_ctx->v = __gsharp_ctx->x;
            }

            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                __gsharp_ctx->p = 1;
                __gsharp_ctx->q = __gsharp_ctx->p;
            }

            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 5;
            return;
        }

        case 5:
        {
            __gsharp_ctx->k = 2;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                __gsharp_ctx->x += __gsharp_ctx->v + __gsharp_ctx->q + __gsharp_ctx->k;
            }

            __gsharp_state = -1;
            return;
        }
    }
}
}