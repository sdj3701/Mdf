@echo off
setlocal

rem One-click Windows bootstrap for the MDF harness.
rem Assumes Unity 2021.3.45f1 and unity-cli are installed, and Mdfproject is open in Unity.
rem Delegates to bootstrap_harness_windows.py, which runs install_git_hooks.py and bootstrap_dev_env.py.

set "ROOT=%~dp0"
pushd "%ROOT%" >nul 2>nul
if errorlevel 1 (
  echo [MDF BOOTSTRAP] Failed to enter repo root: %ROOT%
  exit /b 1
)

if not exist "Mdfproject\ProjectSettings\ProjectVersion.txt" (
  echo [MDF BOOTSTRAP] This BAT must be run from the MDF repo root.
  echo [MDF BOOTSTRAP] Missing Mdfproject\ProjectSettings\ProjectVersion.txt
  popd >nul 2>nul
  exit /b 1
)

set "PY_EXE="
set "PY_ARGS="
call :FindPython
if errorlevel 1 (
  echo [MDF BOOTSTRAP] Python 3.10+ was not found. Trying winget install...
  where winget >nul 2>nul
  if errorlevel 1 (
    echo [MDF BOOTSTRAP] winget is not available. Install Python 3.10+ and rerun this BAT.
    popd >nul 2>nul
    exit /b 1
  )
  winget install --id Python.Python.3.12 -e --source winget --accept-package-agreements --accept-source-agreements
  call :FindPython
  if errorlevel 1 (
    echo [MDF BOOTSTRAP] Python install finished or was attempted, but Python is still not visible to this shell.
    echo [MDF BOOTSTRAP] Open a new terminal and rerun SETUP_MDF_HARNESS.bat.
    popd >nul 2>nul
    exit /b 1
  )
)

set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"

echo [MDF BOOTSTRAP] Repo root: %CD%
echo [MDF BOOTSTRAP] Python: %PY_EXE% %PY_ARGS%
"%PY_EXE%" %PY_ARGS% tools\harness\bootstrap_harness_windows.py %*
set "EXITCODE=%ERRORLEVEL%"

popd >nul 2>nul
exit /b %EXITCODE%

:FindPython
py -3 -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if not errorlevel 1 (
  set "PY_EXE=py"
  set "PY_ARGS=-3"
  exit /b 0
)

python -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if not errorlevel 1 (
  set "PY_EXE=python"
  set "PY_ARGS="
  exit /b 0
)

python3 -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
if not errorlevel 1 (
  set "PY_EXE=python3"
  set "PY_ARGS="
  exit /b 0
)

for /d %%D in ("%LOCALAPPDATA%\Programs\Python\Python3*") do (
  if exist "%%~fD\python.exe" (
    "%%~fD\python.exe" -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
    if not errorlevel 1 (
      set "PY_EXE=%%~fD\python.exe"
      set "PY_ARGS="
      exit /b 0
    )
  )
)

for /d %%D in ("%ProgramFiles%\Python3*") do (
  if exist "%%~fD\python.exe" (
    "%%~fD\python.exe" -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
    if not errorlevel 1 (
      set "PY_EXE=%%~fD\python.exe"
      set "PY_ARGS="
      exit /b 0
    )
  )
)

for /d %%D in ("%ProgramFiles(x86)%\Python3*") do (
  if exist "%%~fD\python.exe" (
    "%%~fD\python.exe" -c "import sys; raise SystemExit(0 if sys.version_info >= (3, 10) else 1)" >nul 2>nul
    if not errorlevel 1 (
      set "PY_EXE=%%~fD\python.exe"
      set "PY_ARGS="
      exit /b 0
    )
  )
)

exit /b 1
