@echo off
setlocal
pushd "%~dp0"

dotnet publish ZCue.csproj --configuration Release --runtime win-x64 --self-contained false --output publish\win-x64
set "PUBLISH_RESULT=%ERRORLEVEL%"
if not "%PUBLISH_RESULT%"=="0" goto publish_failed

powershell.exe -NoProfile -NonInteractive -Command "$ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; [xml]$project = Get-Content -LiteralPath 'ZCue.csproj'; $version = [version]$project.Project.PropertyGroup.Version; $archive = [IO.Path]::GetFullPath(('publish\ZCue-v' + $version + '-win-x64.zip')); [IO.File]::Delete($archive); [IO.Compression.ZipFile]::CreateFromDirectory([IO.Path]::GetFullPath('publish\win-x64'), $archive)"
set "PUBLISH_RESULT=%ERRORLEVEL%"
if not "%PUBLISH_RESULT%"=="0" goto publish_failed

echo.
echo Publish complete: "%CD%\publish\win-x64"
echo Requires .NET 8 Desktop Runtime (x64).
echo Update ZIP created in publish. Attach it to the matching GitHub Release.
popd
exit /b 0

:publish_failed
echo.
echo Publish failed with exit code %PUBLISH_RESULT%.
popd
pause
exit /b %PUBLISH_RESULT%
