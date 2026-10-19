public class Lower_WhileStatement_Snapshot
{
    public void LoweredMethod()
{
    __gsharp_WhileStart_0:
        ;
    if (!(condition))
        goto __gsharp_WhileEnd_0;
    int a = 1;
    Gpu.SyncThreads();
    __gsharp_WhileContinue_0:
        ;
    goto __gsharp_WhileStart_0;
    __gsharp_WhileEnd_0:
        ;
}
}