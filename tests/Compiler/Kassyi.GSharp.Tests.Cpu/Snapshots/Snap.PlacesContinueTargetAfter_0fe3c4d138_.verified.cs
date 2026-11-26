public class LowerForStack_UsingDeclarationAfterContinue_PlacesContinueTargetAfterUsing_Snapshot
{
    public void LoweredMethod()
{
    int t = ThreadIndex.X;
    int g = BlockIndex.X * BlockDimension.X + t;
    for (int i = 0; i < 3; i++)
    {
        __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 0);
        if ((t + i) % 2 == 0)
        {
            goto __gsharp_ReconvergeContinue_0;
        }

        using (var gd = new StackScopeGuard
        {
            Arr = result,
            I = g
        }

        )
        {
            var __gsharp_warp_temp_0 = Warp.ActiveMask();
            result[g] += __gsharp_warp_temp_0 & 0xFu;
        }

        __gsharp_ReconvergeContinue_0:
            ;
        __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 0, "StackScopeKernels.cu.cs(50,9)");
    }
}
}