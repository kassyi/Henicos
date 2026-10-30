public class Lower_Switch_DispatchesEachLabelAndFallsBackToDefault_Snapshot
{
    public void LoweredMethod()
{
    {
        var __gsharp_switch_temp_0 = flag + 1;
        if (__gsharp_switch_temp_0 == 0)
            goto __gsharp_SwitchSection_0_0;
        if (__gsharp_switch_temp_0 == Last)
            goto __gsharp_SwitchSection_0_0;
        goto __gsharp_SwitchSection_0_1;
    }

    __gsharp_SwitchSection_0_0:
        ;
    value = 1;
    Gpu.SyncThreads();
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchSection_0_1:
        ;
    value = 2;
    goto __gsharp_SwitchEnd_0;
    __gsharp_SwitchEnd_0:
        ;
}
}