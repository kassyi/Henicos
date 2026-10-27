public class Lower_SyncGrid_NestedLoop_IsOneStatement_Snapshot
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
            for (__gsharp_ctx->i = 0; __gsharp_ctx->i < 10; __gsharp_ctx->i++)
            {
                __gsharp_ctx->x += __gsharp_ctx->i;
                __gsharp_ctx->x *= 2;
            }

            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->x += 2;
            __gsharp_state = -1;
            return;
        }
    }
}
}