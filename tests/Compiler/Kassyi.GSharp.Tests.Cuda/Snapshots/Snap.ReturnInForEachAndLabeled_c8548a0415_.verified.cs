public class Lower_SyncGrid_ReturnInForEachAndLabeledStatement_JumpsToTheirLabels_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->x = 0;
            __gsharp_ctx->values = [1, 2];
            __gsharp_ctx->__gsharp_sg_done_0 = false;
            foreach (int value in __gsharp_ctx->values)
            {
                if (value == __gsharp_ctx->x)
                {
                    __gsharp_ctx->__gsharp_sg_done_0 = true;
                    goto __gsharp_sg_next_0_0;
                }
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
                Check:
                    if (__gsharp_ctx->x > 3)
                    {
                        __gsharp_ctx->__gsharp_sg_done_0 = true;
                        goto __gsharp_sg_next_0_1;
                    }
            }

            goto case 3;
        }

        case 3:
            __gsharp_sg_next_0_1:
                ;
        {
            __gsharp_yielded_for_grid_sync = true;
            __gsharp_state = 4;
            return;
        }

        case 4:
        {
            if (!__gsharp_ctx->__gsharp_sg_done_0)
            {
                __gsharp_ctx->x += 2;
            }

            __gsharp_state = -1;
            return;
        }
    }
}
}