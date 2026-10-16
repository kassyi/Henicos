extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollNested(GSharpArray<int> data){
int sum = 0;
for(int i = 0;i<2;i++)
{
#pragma unroll
for(int j = 0;j<3;j++)
{
sum+=(i+1)*(j+1);
}
}
data[0]=sum;
}
