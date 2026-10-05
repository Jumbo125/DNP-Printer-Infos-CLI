@echo off
setlocal

dotnet publish "C:\Users\andre\Desktop\photo-software\DEV\DNP2\dnp_info.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true     
if errorlevel 1 exit /b 1

dotnet publish "C:\Users\andre\Desktop\photo-software\DEV\DNP2\dnp_info.csproj" -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true     
if errorlevel 1 exit /b 1

endlocal
echo Fertig.