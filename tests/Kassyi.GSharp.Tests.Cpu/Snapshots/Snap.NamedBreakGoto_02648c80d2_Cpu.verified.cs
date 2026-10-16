inline void NamedBreakGoto(GSharpArray<int> output, int limit){
int sum = 0;
for(int i = 0;i<10;i++)
{
for(int j = 0;j<10;j++)
{
sum+=1;
if(sum>=limit)
{
goto ExitLoops;
}
}
}
ExitLoops:
output[0]=sum;
}
