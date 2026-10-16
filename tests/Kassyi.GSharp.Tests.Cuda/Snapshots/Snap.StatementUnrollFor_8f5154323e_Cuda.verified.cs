extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFor(GSharpArray<int> data){
int sum = 0;
#pragma unroll
for(int i = 0;i<4;i++)
{
sum+=i+1;
}
data[0]=sum;
}
