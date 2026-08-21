call "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat"
nvcc -shared -o ../HenicosCuda.dll kernel.cu cufft.lib -O3 -Xcompiler "/wd 4819"
