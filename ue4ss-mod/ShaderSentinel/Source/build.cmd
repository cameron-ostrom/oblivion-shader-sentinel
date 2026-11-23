@echo off
rem Builds OblivionShaderSentinel.exe from source using the C# compiler that
rem ships with Windows (.NET Framework 4.x). Output lands one folder up,
rem replacing the bundled exe.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /win32icon:"%~dp0icon.ico" /out:"%~dp0..\OblivionShaderSentinel.exe" "%~dp0ShaderSentinel.cs"
if %errorlevel%==0 (echo Build OK) else (echo Build FAILED)
pause
