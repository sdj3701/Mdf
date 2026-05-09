@echo off
setlocal EnableExtensions

rem MDF context packer root launcher.
rem Keep this as the only packer file in the project root.

set "ROOT_DIR=%~dp0"
set "PACK_DIR=%ROOT_DIR%_context_packer"

if /I "%~1"=="--menu" goto RUN_MENU
if /I "%~1"=="menu" goto RUN_MENU
if /I "%~1"=="/menu" goto RUN_MENU

if not exist "%PACK_DIR%\run_context_pack.bat" (
  echo [ERROR] Missing _context_packer\run_context_pack.bat
  echo Extract the full package at the project root.
  pause
  exit /b 1
)

pushd "%ROOT_DIR%" >nul
call "%PACK_DIR%\run_context_pack.bat" %*
set "ERR=%ERRORLEVEL%"
popd >nul

if "%ERR%"=="0" (
  echo.
  echo [OK] Context bundle created. Check: _context_packer\output
) else (
  echo.
  echo [ERROR] Context bundle failed with exit code %ERR%.
)
pause
exit /b %ERR%

:RUN_MENU
if not exist "%PACK_DIR%\menu.bat" (
  echo [ERROR] Missing _context_packer\menu.bat
  pause
  exit /b 1
)
pushd "%ROOT_DIR%" >nul
call "%PACK_DIR%\menu.bat"
set "ERR=%ERRORLEVEL%"
popd >nul
pause
exit /b %ERR%
