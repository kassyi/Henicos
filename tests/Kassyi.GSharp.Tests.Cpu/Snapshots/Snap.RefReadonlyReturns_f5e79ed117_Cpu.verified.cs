inline const int& RefReadonlyReturns(int* __restrict__  a, int* __restrict__  b, bool first){
return ((*(first?&(*a):&(*b))));
}
