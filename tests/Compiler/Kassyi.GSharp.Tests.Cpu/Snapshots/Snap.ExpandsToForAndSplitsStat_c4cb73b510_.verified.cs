public class Lower_ForUnroll_WithBarrier_ExpandsToForAndSplitsStateMachine_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->sum = 0;
            __gsharp_ctx->__gsharp_unroll_0 = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->__gsharp_unroll_0 < 0 + 3))
                goto case 4;
            __gsharp_ctx->sum += __gsharp_ctx->__gsharp_unroll_0;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->__gsharp_unroll_0++;
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