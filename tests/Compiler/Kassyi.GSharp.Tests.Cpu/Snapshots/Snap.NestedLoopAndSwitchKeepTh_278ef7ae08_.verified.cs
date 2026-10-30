public class Lower_Switch_NestedLoopAndSwitchKeepTheirJumps_Snapshot
{
    public void LoweredMethod()
{
    {
        var __gsharp_switch_temp_0 = flag;
        if (__gsharp_switch_temp_0 == 0)
            goto __gsharp_SwitchSection_0_0;
        goto __gsharp_SwitchSection_0_1;
    }

    __gsharp_SwitchSection_0_0:
        ;
    for (int i = 0; i < 2; i++)
    {
        if (i == 1)
        {
            break;
        }
    }

    switch (value)
    {
        case 3:
            break;
        default:
            goto case 3;
    }

    Gpu.SyncThreads();
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchSection_0_1:
        ;
    value = 1;
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchEnd_0:
        ;
}
}