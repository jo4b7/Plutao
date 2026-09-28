@echo off
setlocal
cd /d "%~dp0"

echo [Plutao] Preparando compilacao...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo ERRO: .NET SDK nao foi encontrado no PATH.
  echo Instale o .NET 8 SDK x64 e abra um novo terminal.
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq Plutao.exe" 2>NUL | find /I "Plutao.exe" >NUL
if not errorlevel 1 (
  echo [Plutao] Fechando Plutao.exe para liberar o arquivo de release...
  taskkill /F /IM Plutao.exe >NUL 2>&1
  timeout /t 1 /nobreak >NUL
)

echo [Plutao] Compilando release win-x64...
dotnet publish .\src\Plutao\Plutao.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\release
if errorlevel 1 (
  echo.
  echo Falha ao compilar. Veja a mensagem de erro acima.
  pause
  exit /b 1
)

echo.
echo Pronto: %~dp0release\Plutao.exe
pause
