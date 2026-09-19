@echo off
setlocal
pushd "%~dp0"

dotnet publish TypeSense.csproj --configuration Release --runtime win-x64 --self-contained false --output publish\win-x64
set "PUBLISH_RESULT=%ERRORLEVEL%"
if not "%PUBLISH_RESULT%"=="0" goto publish_failed

echo.
echo Publish complete: "%CD%\publish\win-x64"
echo Requires .NET 8 Desktop Runtime (x64).
popd
exit /b 0

:publish_failed
echo.
echo Publish failed with exit code %PUBLISH_RESULT%.
popd
pause
exit /b %PUBLISH_RESULT%
