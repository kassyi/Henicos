extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFactor(GSharpArray<int> data){
int sum = 0;
#pragma unroll 4
for(int i = 0;i<4;i++)
{
sum+=(i+1)*10;
}
data[0]=sum;
}
