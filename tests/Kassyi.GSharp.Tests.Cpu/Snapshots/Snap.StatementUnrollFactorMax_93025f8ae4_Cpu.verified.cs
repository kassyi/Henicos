extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFactorMax(GSharpArray<int> data){
int sum = 0;
#pragma unroll 2147483647
for(int i = 0;i<4;i++)
{
sum+=i;
}
data[0]=sum;
}
