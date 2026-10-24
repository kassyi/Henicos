public class Lower_Iterator_YieldBreak_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            if (!(__gsharp_ctx->x < 0))
                goto case 1;
            __gsharp_state = -2;
            return false;
            goto case 1;
        }

        case 1:
        {
            __gsharp_current = __gsharp_ctx->x;
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