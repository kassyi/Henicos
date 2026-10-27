public class Lower_SyncGrid_BlockBarrierInBody_KeepsBothBarriers_Snapshot
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
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            __gsharp_ctx->x += 2;
            __gsharp_state = -1;
            return;
        }
    }
}
}