public class Lower_LabeledJumpsOfABarrierLoop_BecomeGotos_Snapshot
{
    // t -> __gsharp_id_1_t
    // s -> __gsharp_id_2_s
    // i -> __gsharp_id_4_i
    // j -> __gsharp_id_5_j
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->s = 0;
            outer:
                ;
            __gsharp_ctx->i = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->i < 4))
                goto case 8;
            __gsharp_ctx->j = 0;
            goto case 2;
        }

        case 2:
        {
            if (!(__gsharp_ctx->j < 4))
                goto case 5;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 3;
            return;
        }

        case 3:
        {
            if (__gsharp_ctx->j == 1 && __gsharp_ctx->i == 2)
                goto case 9;
            if (__gsharp_ctx->j == 2)
                goto case 6;
            __gsharp_ctx->s += (__gsharp_ctx->i * 4) + __gsharp_ctx->j;
            goto case 4;
        }

        case 4:
        {
            __gsharp_ctx->j++;
            goto case 2;
            goto case 5;
        }

        case 5:
        {
            __gsharp_ctx->s += 100;
            goto case 6;
        }

        case 6:
        {
            goto case 7;
        }

        case 7:
        {
            __gsharp_ctx->i++;
            goto case 1;
            goto case 8;
        }

        case 8:
        {
            goto case 9;
        }

        case 9:
        {
            __gsharp_ctx->o[__gsharp_ctx->t] = __gsharp_ctx->s + __gsharp_ctx->t;
            __gsharp_state = -1;
            return;
        }
    }
}
}