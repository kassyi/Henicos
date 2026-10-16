extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFactor0(GSharpArray<int> data){
int sum = 0;
#pragma unroll
for(int i = 0;i<4;i++)
{
sum+=i;
}
data[0]=sum;
}
