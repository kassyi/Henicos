public class Lower_ForStatement_WithNestedBarrierFreeForBreakAndContinue_Snapshot
{
    public void LoweredMethod()
{
    int i = 0;
    __gsharp_ForStart_0:
        ;
    if (!(i < 4))
        goto __gsharp_ForEnd_0;
    for (int j = 0; j < 4; j++)
    {
        if (j == 1)
            continue;
        if (j == 2)
            break;
    }

    Gpu.SyncThreads();
    __gsharp_ForContinue_0:
        ;
    i++;
    goto __gsharp_ForStart_0;
    __gsharp_ForEnd_0:
        ;
}
}