public class Lower_Iterator_VariablesAcrossYield_Snapshot
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
            int c = __gsharp_ctx->a + 3;
            __gsharp_state = -2;
            return false;
        }
    }
}
}