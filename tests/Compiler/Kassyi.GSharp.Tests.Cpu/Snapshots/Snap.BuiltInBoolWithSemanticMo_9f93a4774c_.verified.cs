public class ConditionalRewriter_BuiltInBoolWithSemanticModel_KeepsBuiltInForm_Snapshot
{
    public void LoweredMethod()
{
    var __gsharp_cond_temp_0 = flag;
    if (__gsharp_cond_temp_0)
    {
        __gsharp_cond_temp_0 = Warp.ShuffleSync(0xFFFFFFFFu, value, 1) > 0;
    }

    bool okAnd = __gsharp_cond_temp_0;
    var __gsharp_cond_temp_1 = flag;
    if (!__gsharp_cond_temp_1)
    {
        __gsharp_cond_temp_1 = Warp.ShuffleSync(0xFFFFFFFFu, value, 1) > 0;
    }

    bool okOr = __gsharp_cond_temp_1;
}
}