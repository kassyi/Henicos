extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollVariableFactor(GSharpArray<int> data){
int sum = 0;
int factor = 4;
for(int i = 0;i<4;i++)
{
sum+=i;
}
data[0]=sum;
}

// GSH0016