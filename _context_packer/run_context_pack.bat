@echo off
setlocal EnableExtensions

set "SCRIPT=%~dp0unity_context_pack.py"
if not exist "%SCRIPT%" (
  echo [ERROR] Cannot find %SCRIPT%
  exit /b 1
)

set "PY_EXE="
where py >nul 2>nul
if not errorlevel 1 set "PY_EXE=py -3"
if not defined PY_EXE (
  where python >nul 2>nul
  if not errorlevel 1 set "PY_EXE=python"
)
if not defined PY_EXE (
  echo [ERROR] Python 3 was not found. Install Python 3 or add it to PATH.
  exit /b 1
)

%PY_EXE% "%SCRIPT%" %*
exit /b %ERRORLEVEL%
