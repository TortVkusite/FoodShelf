@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo Установите .NET SDK 9 или 10 с https://dotnet.microsoft.com/download/dotnet/9.0
  pause
  exit /b 1
)
echo Запуск FoodShelf. После сообщения Now listening откройте http://localhost:5088
echo Чтобы остановить приложение, нажмите Ctrl+C.
dotnet run --project src/FoodShelf.Api -- --Demo=true --urls=http://localhost:5088
pause

