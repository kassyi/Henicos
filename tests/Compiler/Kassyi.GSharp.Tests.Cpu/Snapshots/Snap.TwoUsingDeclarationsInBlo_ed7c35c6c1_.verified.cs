public class LowerForStack_TwoUsingDeclarationsInBlock_NestsInnerInsideOuter_Snapshot
{
    public void LoweredMethod()
{
    int t = ThreadIndex.X;
    int g = BlockIndex.X * BlockDimension.X + t;
    __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 0);
    for (int i = 0; i < 3; i++)
    {
        __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 1);
        using (var g1 = new StackScopeGuard
        {
            Arr = result,
            I = g
        }

        )
        {
            if (t == 3 && i == 1)
            {
                __gsharp_stack_warp_region_leave(__gsharp_warp_frame, 1);
                break;
            }

            using (var g2 = new StackScopeGuard
            {
                Arr = result,
                I = g
            }

            )
            {
                if ((t + i) % 2 == 0)
                {
                    goto __gsharp_ReconvergeContinue_1;
                }

                var __gsharp_warp_temp_2 = Warp.ActiveMask();
                result[g] += __gsharp_warp_temp_2 & 0xFu;
            }
        }

        __gsharp_ReconvergeContinue_1:
            ;
        __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 1, "StackScopeKernels.cu.cs(185,9)");
    }

    __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 0, "StackScopeKernels.cu.cs(185,9)");
    var __gsharp_arg_temp_0 = result[g];
    var __gsharp_warp_temp_1 = Warp.ActiveMask();
    __gsharp_arg_temp_0 += (__gsharp_warp_temp_1 & 0xFFu) << 8;
    result[g] = __gsharp_arg_temp_0;
}
}