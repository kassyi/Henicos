public class Lower_DoStatement_Snapshot
{
    public void LoweredMethod()
{
    _DoStart_0:
        ;
    Gpu.SyncThreads();
    _DoContinue_0:
        ;
    if (condition)
        goto _DoStart_0;
    _DoEnd_0:
        ;
}
}