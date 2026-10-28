public class Lower_ForUnroll_TwoInSameMethod_UsesDistinctLoopCounters_Snapshot
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
            if (!(__gsharp_ctx->__gsharp_unroll_0 < 0 + 2))
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
            __gsharp_ctx->__gsharp_unroll_1 = 1;
            goto case 5;
        }

        case 5:
        {
            if (!(__gsharp_ctx->__gsharp_unroll_1 < 1 + 3))
                goto case 8;
            __gsharp_ctx->sum += __gsharp_ctx->__gsharp_unroll_1;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 6;
            return;
        }

        case 6:
        {
            goto case 7;
        }

        case 7:
        {
            __gsharp_ctx->__gsharp_unroll_1++;
            goto case 5;
            goto case 8;
        }

        case 8:
        {
            __gsharp_state = -1;
            return;
        }
    }
}
}