public class Lower_Iterator_StraightLine_TwoYields_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->a = 1;
            __gsharp_current = __gsharp_ctx->a;
            __gsharp_state = 1;
            return true;
        }

        case 1:
        {
            __gsharp_ctx->b = __gsharp_ctx->a + 2;
            __gsharp_current = __gsharp_ctx->b;
            __gsharp_state = 2;
            return true;
        }

        case 2:
        {
            __gsharp_state = -2;
            return false;
        }
    }
}
}