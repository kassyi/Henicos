public class Lower_ForUnroll_WithoutBarrier_RemainsMacroCall_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->sum = 0;
            Gpu.ForUnroll(0, 3, iter =>
            {
                __gsharp_ctx->sum += iter;
            });
            __gsharp_state = 1;
            return;
        }

        case 1:
        {
            __gsharp_state = -1;
            return;
        }
    }
}
}