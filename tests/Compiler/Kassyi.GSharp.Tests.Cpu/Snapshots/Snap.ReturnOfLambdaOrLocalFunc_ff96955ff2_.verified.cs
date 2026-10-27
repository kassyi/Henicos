public class Lower_SyncGrid_ReturnOfLambdaOrLocalFunction_IsNotRewritten_Snapshot
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
            Gpu.ForUnroll(0, 2, i =>
            {
                if (i == 1)
                    return;
                __gsharp_ctx->x += i;
            });
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            void Step()
            {
                return;
            }

            Step();
            __gsharp_state = -1;
            return;
        }
    }
}
}