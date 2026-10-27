public class Lower_SyncGrid_BodyInLoopOfBody_IsExpandedToo_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->x += 1;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->k = 0;
            goto case 2;
        }

        case 2:
        {
            if (!(__gsharp_ctx->k < 2))
                goto case 5;
            __gsharp_ctx->x += __gsharp_ctx->k;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_ctx->x *= 2;
            goto case 4;
        }

        case 4:
        {
            __gsharp_ctx->k++;
            goto case 2;
            goto case 5;
        }

        case 5:
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            __gsharp_ctx->x += 2;
            __gsharp_state = -1;
            return;
        }
    }
}
}