extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollWhile(GSharpArray<int> data){
int count = 0;
int sum = 0;
#pragma unroll
while(count<3)
{
sum+=5;
count++;
}
data[0]=sum;
}
