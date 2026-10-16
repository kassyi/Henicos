extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollMisplacedEnd(GSharpArray<int> data){
int sum = 0;
for(int i = 0;i<4;i++)
{
sum+=i;
}
data[0]=sum;
}

// GSH0017