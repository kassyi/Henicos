extern "C" __global__ void StringsAndAttributesTarget_NameOfExpression(GSharpArray<int> output){
System_String name = nameof(output);
output[0]=name.length;
}

// GSH0002