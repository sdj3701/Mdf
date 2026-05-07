@echo off
setlocal EnableExtensions

:MENU
echo.
echo MDF Context Packer
echo ==================
echo 1. Default: scripts-plus-context
echo 2. Scripts only
echo 3. Trimmed project
echo 4. Harness only
echo 5. Failure artifacts
echo 6. List profiles
echo 0. Exit
echo.
set /p CHOICE=Select: 

if "%CHOICE%"=="1" call "%~dp0run_context_pack.bat" --profile scripts-plus-context & exit /b %ERRORLEVEL%
if "%CHOICE%"=="2" call "%~dp0run_context_pack.bat" --profile scripts & exit /b %ERRORLEVEL%
if "%CHOICE%"=="3" call "%~dp0run_context_pack.bat" --profile trimmed-project & exit /b %ERRORLEVEL%
if "%CHOICE%"=="4" call "%~dp0run_context_pack.bat" --profile harness-only & exit /b %ERRORLEVEL%
if "%CHOICE%"=="5" goto FAILURE
if "%CHOICE%"=="6" call "%~dp0run_context_pack.bat" --list-profiles & goto MENU
if "%CHOICE%"=="0" exit /b 0
echo Invalid choice.
goto MENU

:FAILURE
echo.
set /p ARTIFACT_PATH=Enter artifact path, e.g. artifacts\mp\case-folder: 
if not defined ARTIFACT_PATH (
  echo Artifact path is required.
  goto MENU
)
call "%~dp0run_context_pack.bat" --profile failure-artifacts --artifact-path "%ARTIFACT_PATH%"
exit /b %ERRORLEVEL%
