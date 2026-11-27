public class ConditionalRewriter_UserDefinedOrWithSplit_LowersToTruthTestAndBitwiseOr_Snapshot
{
    public void LoweredMethod()
{
    int t = ThreadIndex.X;
    TruthValue c = default;
    var __gsharp_cond_temp_0 = default;
    __gsharp_cond_temp_0 = new TruthValue(input[t]);
    var __gsharp_cond_temp_1 = default;
    if (__gsharp_truth_test(__gsharp_cond_temp_0))
    {
        __gsharp_cond_temp_1 = __gsharp_cond_temp_0;
    }
    else
    {
        __gsharp_cond_temp_1 = __gsharp_cond_temp_0 | MaskValue(count, t);
    }

    c = __gsharp_cond_temp_1;
    output[t] = c.Value;
}
}