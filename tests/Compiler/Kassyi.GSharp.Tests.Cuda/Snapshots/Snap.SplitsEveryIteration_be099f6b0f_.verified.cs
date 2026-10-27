public class Lower_SyncGrid_InsideLoop_SplitsEveryIteration_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->k = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->k < 2))
                goto case 4;
            __gsharp_ctx->x += __gsharp_ctx->k;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->x *= 2;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->k++;
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_state = -1;
            return;
        }
    }
}
}