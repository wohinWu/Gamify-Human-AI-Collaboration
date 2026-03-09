@echo off
cd /d "%~dp0"

echo Starting FastAPI service on http://127.0.0.1:8000
uvicorn main:app --host 127.0.0.1 --port 8000

echo.
pause
