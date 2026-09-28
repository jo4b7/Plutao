@echo off
setlocal
cd /d "%~dp0"

echo [Plutao] Compilando release win-x64...
dotnet publish .\src\Plutao\Plutao.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release
if errorlevel 1 (
  echo.
  echo Falha ao compilar. Instale o .NET 8 SDK:
  echo https://dotnet.microsoft.com/download/dotnet/8.0
  pause
  exit /b 1
)

echo.
echo Pronto: %~dp0release\Plutao.exe
pause
