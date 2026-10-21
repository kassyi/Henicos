public class Lower_ForStatement_WithBreakAndContinue_Snapshot
{
    public void LoweredMethod()
{
    int i = 0;
    _ForStart_0:
        ;
    if (!(i < 10))
        goto _ForEnd_0;
    if (i == 5)
        goto _ForContinue_0;
    if (i == 8)
        goto _ForEnd_0;
    Gpu.SyncThreads();
    _ForContinue_0:
        ;
    i++;
    goto _ForStart_0;
    _ForEnd_0:
        ;
}
}