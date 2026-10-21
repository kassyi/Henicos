public class Lower_ForStatement_WithBreakAndContinue_Snapshot
{
    public void LoweredMethod()
{
    int i = 0;
    __gsharp_ForStart_0:
        ;
    if (!(i < 10))
        goto __gsharp_ForEnd_0;
    if (i == 5)
        goto __gsharp_ForContinue_0;
    if (i == 8)
        goto __gsharp_ForEnd_0;
    Gpu.SyncThreads();
    __gsharp_ForContinue_0:
        ;
    i++;
    goto __gsharp_ForStart_0;
    __gsharp_ForEnd_0:
        ;
}
}