@echo off
setlocal
cd /d "%~dp0"

echo [Plutao] Compilando release win-x64...
dotnet publish .\src\Plutao\Plutao.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release
if errorlevel 1 (
  echo.
  echo Falha ao compilar. Verifique se o .NET 8 SDK x64 esta instalado.
  pause
  exit /b 1
)

echo.
echo Pronto: %~dp0release\Plutao.exe
pause
