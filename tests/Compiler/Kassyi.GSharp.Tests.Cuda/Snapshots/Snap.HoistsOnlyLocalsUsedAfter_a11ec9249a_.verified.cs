public class Managed_CrossLabelWrite_HoistsOnlyLocalsUsedAfterTheLabel_Snapshot
{
    // tid -> tid
    // value -> value
    // extra -> extra
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->value = __gsharp_ctx->data[__gsharp_ctx->tid];
            if (!(__gsharp_ctx->flag != 0))
                goto case 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->value += 1;
            goto case 3;
            goto case 2;
        }

        case 2:
        {
            int before;
            before = 3;
            __gsharp_ctx->value += before;
            __gsharp_ctx->extra = 7;
            __gsharp_ctx->value += __gsharp_ctx->extra;
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->extra = __gsharp_ctx->value * 2;
            __gsharp_ctx->data[__gsharp_ctx->tid] = __gsharp_ctx->extra;
            __gsharp_state = -1;
            return;
        }
    }
}
}