public class Lower_SyncGrid_LocalFunction_DeclarationIsNotAStatementGap_Snapshot
{
    public void LoweredMethod()
{
    void Step()
    {
        __gsharp_ctx->x += 11;
    }

    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            Step();
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->x += 33;
            __gsharp_state = -1;
            return;
        }
    }
}
}