public class Lower_ForUnroll_WithRefContext_SubstitutesContextArgument_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->acc = default;
            __gsharp_ctx->__gsharp_unroll_0 = 1;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->__gsharp_unroll_0 < 1 + 3))
                goto case 4;
            __gsharp_ctx->acc.Total += __gsharp_ctx->__gsharp_unroll_0;
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