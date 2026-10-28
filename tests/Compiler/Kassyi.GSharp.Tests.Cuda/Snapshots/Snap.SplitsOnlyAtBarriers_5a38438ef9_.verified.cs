public class Lower_SameNameLabel_SplitsOnlyAtBarriers_Snapshot
{
    // t -> __gsharp_id_1_t
    // acc -> __gsharp_id_2_acc
    // step -> __gsharp_id_3_step
    // step -> __gsharp_id_5_step
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->t = ThreadIndex.X;
            __gsharp_ctx->acc = __gsharp_ctx->t;
            __gsharp_ctx->step = 10;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            if ((__gsharp_ctx->t & 1) == 0)
            {
                __gsharp_ctx->acc += __gsharp_ctx->step;
                goto done;
            }

            __gsharp_ctx->acc += 100;
            done:
                ;
            __gsharp_ctx->step = 1000;
            if ((__gsharp_ctx->t & 1) != 0)
            {
                __gsharp_ctx->acc += __gsharp_ctx->step;
                goto done;
            }

            __gsharp_ctx->acc += 2000;
            done:
                ;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            output[__gsharp_ctx->t] = __gsharp_ctx->acc;
            __gsharp_state = -1;
            return;
        }
    }
}
}