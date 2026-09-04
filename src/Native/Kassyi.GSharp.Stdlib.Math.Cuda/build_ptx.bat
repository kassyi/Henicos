@echo off
setlocal
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
    set "VS_PATH=%%i"
)
set "VCVARS_PATH=%VS_PATH%\VC\Auxiliary\Build\vcvars64.bat"
call "%VCVARS_PATH%"
set "NVCC_CMD=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.3\bin\nvcc.exe"
"%NVCC_CMD%" -ptx bench_main.cu -O3 -arch=sm_86 -Xcompiler "/wd 4819 /Zc:preprocessor"
endlocal
