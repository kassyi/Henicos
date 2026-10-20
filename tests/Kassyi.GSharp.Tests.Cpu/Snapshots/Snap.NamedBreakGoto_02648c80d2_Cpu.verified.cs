inline void NamedBreakGoto(GSharpArray<int> __gsharp_id_0_output, int __gsharp_id_1_limit){
int __gsharp_id_2_sum = 0;
for(int __gsharp_id_3_i = 0;__gsharp_id_3_i<10;__gsharp_id_3_i++)
{
for(int __gsharp_id_4_j = 0;__gsharp_id_4_j<10;__gsharp_id_4_j++)
{
__gsharp_id_2_sum+=1;
if(__gsharp_id_2_sum>=__gsharp_id_1_limit)
{
goto __gsharp_id_5_ExitLoops;
}
}
}
__gsharp_id_5_ExitLoops:
__gsharp_id_0_output[0]=__gsharp_id_2_sum;
}
