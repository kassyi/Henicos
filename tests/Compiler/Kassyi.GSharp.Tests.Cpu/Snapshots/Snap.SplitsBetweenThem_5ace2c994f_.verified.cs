public class Lower_SyncGrid_TwoStatements_SplitsBetweenThem_Snapshot
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
            __gsharp_ctx->x += 2;
            __gsharp_state = -1;
            return;
        }
    }
}
}