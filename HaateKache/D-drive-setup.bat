@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo Exact steps this script runs:
echo   mkdir "D:\হাতে কাছে"
echo   xcopy /E /I /Y "%~dp0*" "D:\হাতে কাছে\"
echo   del /f /q app.py main.py assets.py wsgi.py app.bat requirements.txt main.db
echo   rmdir /s /q templates static __pycache__
echo   then double-click "D:\হাতে কাছে\start.bat"
echo.

if not exist D:\ (
  echo D: drive was not found. / D: ড্রাইভ পাওয়া যায়নি।
  pause
  exit /b 1
)

if not exist "D:\হাতে কাছে\" mkdir "D:\হাতে কাছে"
xcopy "%~dp0*" "D:\হাতে কাছে\" /E /I /Y
if errorlevel 1 (
  echo Copy failed. / কপি হয়নি।
  pause
  exit /b 1
)

del /f /q "D:\হাতে কাছে\app.py" 2>nul
del /f /q "D:\হাতে কাছে\main.py" 2>nul
del /f /q "D:\হাতে কাছে\assets.py" 2>nul
del /f /q "D:\হাতে কাছে\wsgi.py" 2>nul
del /f /q "D:\হাতে কাছে\app.bat" 2>nul
del /f /q "D:\হাতে কাছে\requirements.txt" 2>nul
del /f /q "D:\হাতে কাছে\main.db" 2>nul
if exist "D:\হাতে কাছে\templates" rmdir /s /q "D:\হাতে কাছে\templates"
if exist "D:\হাতে কাছে\static" rmdir /s /q "D:\হাতে কাছে\static"
if exist "D:\হাতে কাছে\__pycache__" rmdir /s /q "D:\হাতে কাছে\__pycache__"

echo.
echo Ready. Double-click "D:\হাতে কাছে\start.bat"
echo তৈরি। "D:\হাতে কাছে\start.bat"-এ ডবল-ক্লিক করুন।
pause
