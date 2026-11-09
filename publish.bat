@echo off
setlocal

pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet was not found. Install .NET 10 SDK and add it to PATH.
    popd
    pause
    exit /b 1
)

dotnet --list-sdks | findstr /b /c:"10." >nul
if errorlevel 1 (
    echo [ERROR] .NET 10 SDK is required.
    popd
    pause
    exit /b 1
)

echo Publishing WhimGroupWin - Release, win-x64, self-contained, portable runtime folder...
dotnet publish "WhimGroupWin.csproj" -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -p:PortableRuntimeLayout=true -p:DebugType=None -p:DebugSymbols=false -o "publish\win-x64-portable\runtime"
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed. See the output above.
    popd
    pause
    exit /b 1
)

echo.
echo [OK] Output: %CD%\publish\win-x64-portable
echo Run WhimGroupWin.exe from that directory.
echo Distribute the entire directory, including runtime. No .NET installation is required.
echo Configuration JSON files are saved next to WhimGroupWin.exe.
echo Runtime libraries are loaded directly without extraction.
popd
pause
exit /b 0