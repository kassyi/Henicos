public class Lower_BranchExitLabel_GotoBecomesGotoCase_Snapshot
{
    // tid -> __gsharp_id_2_tid
    // value -> __gsharp_id_3_value
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->value = data[__gsharp_ctx->tid];
            if (!(flag != 0))
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
            __gsharp_ctx->value += 10;
            goto case 3;
        }

        case 3:
        {
            data[__gsharp_ctx->tid] = __gsharp_ctx->value * 2;
            __gsharp_state = -1;
            return;
        }
    }
}
}