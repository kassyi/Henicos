inline void RefConditionalExpression(GSharpArray<int> a, GSharpArray<int> b, bool useFirst){
IncrementRef(&((*(useFirst?&a[0]:&b[0]))));
}
