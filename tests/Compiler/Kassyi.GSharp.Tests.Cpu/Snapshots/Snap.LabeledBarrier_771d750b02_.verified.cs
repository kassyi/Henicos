public class Lower_LabeledBarrier_SplitsState_Snapshot
{
    // tid -> __gsharp_id_1_tid
    // buffer -> __gsharp_id_2_buffer
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->tid = ThreadIndex.X;
            __gsharp_ctx->buffer = new SharedMemory<int>(4);
            __gsharp_ctx->buffer[__gsharp_ctx->tid] = __gsharp_ctx->tid + 1;
            sync:
                ;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_ctx->output[__gsharp_ctx->tid] = __gsharp_ctx->buffer[(__gsharp_ctx->tid + 1) % 4];
            __gsharp_state = -1;
            return;
        }
    }
}
}