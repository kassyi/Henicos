public class LowerForStack_LabeledUsingDeclaration_KeepsLabelOnUsingStatement_Snapshot
{
    public void LoweredMethod()
{
    int t = ThreadIndex.X;
    __gsharp_stack_warp_region_enter(__gsharp_warp_frame, 0);
    Scoped:
        ;
    using (var gd = new StackScopeLoweringGuard
    {
        Arr = r,
        I = t
    }

    )
    {
        if (r[t] == 0u)
        {
            r[t] = 1u;
            goto Scoped;
        }

        __gsharp_stack_warp_reconverge(__gsharp_warp_frame, 0, "StackScopeLoweringCases.cs(23,9)");
        var __gsharp_arg_temp_0 = r[t];
        var __gsharp_warp_temp_1 = Warp.ActiveMask();
        __gsharp_arg_temp_0 += __gsharp_warp_temp_1;
        r[t] = __gsharp_arg_temp_0;
    }
}
}