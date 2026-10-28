public class Managed_SameNameLabel_RenamesLabelsAndGotos_Snapshot
{
    // t -> t
    // acc -> acc
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
            __gsharp_ctx->__gsharp_id_3_step = 10;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            if ((__gsharp_ctx->t & 1) == 0)
            {
                __gsharp_ctx->acc += __gsharp_ctx->__gsharp_id_3_step;
                goto __gsharp_label_1_done;
            }

            __gsharp_ctx->acc += 100;
            __gsharp_label_1_done:
                ;
            __gsharp_ctx->__gsharp_id_5_step = 1000;
            if ((__gsharp_ctx->t & 1) != 0)
            {
                __gsharp_ctx->acc += __gsharp_ctx->__gsharp_id_5_step;
                goto __gsharp_label_2_done;
            }

            __gsharp_ctx->acc += 2000;
            __gsharp_label_2_done:
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