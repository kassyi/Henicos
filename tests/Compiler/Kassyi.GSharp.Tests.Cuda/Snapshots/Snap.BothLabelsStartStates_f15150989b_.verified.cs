public class Managed_ChainedLabel_BothLabelsStartStates_Snapshot
{
    // tid -> tid
    // x -> x
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->x = data[__gsharp_ctx->tid];
            if (!(flag != 0))
                goto case 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->x += 100;
            goto case 4;
            goto case 2;
        }

        case 2:
        {
            __gsharp_ctx->x += 1;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->x += 2;
            goto case 4;
        }

        case 4:
        {
            __gsharp_ctx->x += 3;
            if (__gsharp_ctx->x < 20)
            {
                goto case 3;
            }

            data[__gsharp_ctx->tid] = __gsharp_ctx->x;
            __gsharp_state = -1;
            return;
        }
    }
}
}