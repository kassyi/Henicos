public class Lower_Iterator_YieldInForStatement_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->i = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->i < __gsharp_ctx->count))
                goto case 4;
            __gsharp_current = __gsharp_ctx->start + __gsharp_ctx->i;
            __gsharp_state = 2;
            return true;
        }

        case 2:
        {
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->i++;
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_state = -2;
            return false;
        }
    }
}
}