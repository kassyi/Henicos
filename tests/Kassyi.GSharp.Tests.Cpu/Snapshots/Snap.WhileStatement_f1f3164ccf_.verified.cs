public class Lower_WhileStatement_Snapshot
{
    public void LoweredMethod()
{
    _WhileStart_0:
        ;
    if (!(condition))
        goto _WhileEnd_0;
    int a = 1;
    Gpu.SyncThreads();
    _WhileContinue_0:
        ;
    goto _WhileStart_0;
    _WhileEnd_0:
        ;
}
}