public class LowerForStack_FixedInLoopWithContinue_LeavesRegionInsideFixed_Snapshot
{
    public void LoweredMethod()
{
    int t = ThreadIndex.X;
    int g = BlockIndex.X * BlockDimension.X + t;
    for (int i = 0; i < 3; i++)
    {
        __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 0);
        if ((t + i) % 3 == 0)
        {
            goto __gsharp_ReconvergeContinue_0;
        }

        fixed (int* p = data)
        {
            if ((t + i) % 3 == 1)
            {
                goto __gsharp_ReconvergeContinue_0;
            }

            var __gsharp_cond_temp_0 = default;
            __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 1);
            if (t > 2)
            {
                var __gsharp_warp_temp_0 = Warp.ActiveMask();
                __gsharp_cond_temp_0 = __gsharp_warp_temp_0;
            }
            else
            {
                __gsharp_cond_temp_0 = null;
            }

            __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 1, "StackScopeKernels.cu.cs(218,13)");
            uint? m = __gsharp_cond_temp_0;
            result[g] = result[g] * 3u + (uint)p[g] + (m ?? 9u);
        }

        __gsharp_ReconvergeContinue_0:
            ;
        __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 0, "StackScopeKernels.cu.cs(210,9)");
    }
}
}