public class Lower_SyncGrid_LocalAcrossGaps_IsHoisted_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->mine = __gsharp_ctx->x + 1;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->x += __gsharp_ctx->mine;
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->x *= __gsharp_ctx->mine;
            __gsharp_state = -1;
            return;
        }
    }
}
}