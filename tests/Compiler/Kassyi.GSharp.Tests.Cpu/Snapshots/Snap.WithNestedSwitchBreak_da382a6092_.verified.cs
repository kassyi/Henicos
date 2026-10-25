public class Lower_ForStatement_WithNestedSwitchBreak_Snapshot
{
    public void LoweredMethod()
{
    int sum = 0;
    int i = 0;
    __gsharp_ForStart_0:
        ;
    if (!(i < 4))
        goto __gsharp_ForEnd_0;
    switch (i)
    {
        case 0:
            sum += 1;
            break;
        default:
            sum += 2;
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