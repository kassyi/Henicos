public class Lower_Iterator_YieldInIfStatement_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            if (!(__gsharp_ctx->flag))
                goto case 2;
            __gsharp_current = 10;
            __gsharp_state = 1;
            return true;
        }

        case 1:
        {
            goto case 4;
            goto case 2;
        }

        case 2:
        {
            __gsharp_current = 20;
            __gsharp_state = 3;
            return true;
        }

        case 3:
        {
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