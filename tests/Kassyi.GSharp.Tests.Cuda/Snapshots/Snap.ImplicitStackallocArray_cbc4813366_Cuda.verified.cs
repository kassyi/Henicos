extern "C" __global__ void MicroModifiersTarget_ImplicitStackallocArray(GSharpArray<int> output){
int data[3]={ 10, 20, 30 };
output[0]=data[1]+3;
}
