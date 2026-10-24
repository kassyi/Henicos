public class Lower_Iterator_YieldInArrayForEach_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->__gsharp_foreach_src_0 = __gsharp_ctx->data;
            __gsharp_ctx->__gsharp_foreach_i_0 = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->__gsharp_foreach_i_0 < __gsharp_ctx->__gsharp_foreach_src_0.Length))
                goto case 4;
            __gsharp_ctx->x = __gsharp_ctx->__gsharp_foreach_src_0[__gsharp_ctx->__gsharp_foreach_i_0];
            __gsharp_current = __gsharp_ctx->x * 2;
            __gsharp_state = 2;
            return true;
        }

        case 2:
        {
            goto case 3;
        }

        case 3:
        {
            __gsharp_ctx->__gsharp_foreach_i_0++;
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