public class ConditionalRewriter_LiftedUserDefinedAndWithSplit_KeepsBuiltInForm_Snapshot
{
    public void LoweredMethod()
{
    LiftedTruth? result = default;
    var __gsharp_cond_temp_0 = left;
    if (__gsharp_cond_temp_0)
    {
        __gsharp_cond_temp_0 = (Warp.ActiveMask() != 0u ? right : null);
    }

    result = __gsharp_cond_temp_0;
}
}