extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFactor1(GSharpArray<int> data){
int sum = 0;
#pragma unroll 1
for(int i = 0;i<4;i++)
{
sum+=i;
}
data[0]=sum;
}
