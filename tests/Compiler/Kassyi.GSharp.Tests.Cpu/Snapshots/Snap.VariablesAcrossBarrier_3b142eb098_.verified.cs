public class Hoist_VariablesAcrossBarrier_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->a = 1;
            __gsharp_ctx->b = 2;
            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            int c = __gsharp_ctx->a + 3; // 'a' is hoisted (accessed via ctx->a), 'c' is kept local.
            __gsharp_state = -1;
            return;
        }
    }
}
}