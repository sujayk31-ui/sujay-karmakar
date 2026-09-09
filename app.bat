@echo off
cd /d "%~dp0"

echo.
echo  Starting Asset Tracking...
echo  Address: http://127.0.0.1:5000
echo  Do not close this window while you are using the app.
echo  Press Ctrl+C to stop.
echo.

where python >nul 2>&1
if errorlevel 1 (
  echo Python was not found. Install Python and add it to PATH.
  pause
  exit /b 1
)

start "" cmd /c "timeout /t 2 /nobreak >nul & start http://127.0.0.1:5000"
python wsgi.py
if errorlevel 1 (
  echo.
  echo The server did not start. Check that Python and waitress are installed.
  echo   pip install -r requirements.txt
  pause
)
