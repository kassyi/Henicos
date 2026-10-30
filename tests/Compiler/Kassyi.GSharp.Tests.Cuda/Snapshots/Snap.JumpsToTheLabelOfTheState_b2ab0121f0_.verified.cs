public class Lower_ForStatement_ContinueInSwitch_JumpsToTheLabelOfTheState_Snapshot
{
    public void LoweredMethod()
{
    switch (__gsharp_state)
    {
        case 0:
        {
            __gsharp_ctx->sum = 0;
            __gsharp_ctx->i = 0;
            goto case 1;
        }

        case 1:
        {
            if (!(__gsharp_ctx->i < 4))
                goto case 4;
            switch (__gsharp_ctx->i)
            {
                case 0:
                    goto __gsharp_ForContinue_0;
                case 3:
                    __gsharp_ctx->sum += 100;
                    break;
                default:
                    break;
            }

            __gsharp_yielded_for_block_barrier = true;
            __gsharp_state = 2;
            return;
        }

        case 2:
        {
            __gsharp_ctx->sum += 1;
            goto case 3;
        }

        case 3:
            __gsharp_ForContinue_0:
                ;
        {
            __gsharp_ctx->i++;
            goto case 1;
            goto case 4;
        }

        case 4:
        {
            __gsharp_state = -1;
            return;
        }
    }
}
}