@echo off
chcp 65001 >nul
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo.
  echo .NET 8 SDK পাওয়া যায়নি। / The .NET 8 SDK was not found.
  echo ইনস্টল করুন: https://dotnet.microsoft.com/download/dotnet/8.0
  echo Install the .NET 8 SDK, then double-click start.bat again.
  echo তারপর আবার start.bat-এ ডবল-ক্লিক করুন।
  echo.
  pause
  exit /b 1
)

echo.
echo Haate Kache / হাতে কাছে
echo Starting http://localhost:5080
echo Logs stay in this window. Close the window to stop the server.
echo.

start "Haate Kache browser" cmd /c "timeout /t 8 /nobreak >nul & start http://localhost:5080"
dotnet run --project src\HaateKache.Web --urls http://localhost:5080

echo.
echo The server stopped. / সার্ভার বন্ধ হয়েছে।
pause
