mkdir out

"C:\Program Files\MSBuild\12.0\Bin\MSBuild.exe" "src\ErrorControlSystem.sln" /t:Clean /t:Rebuild /property:Configuration=Release /verbosity:normal
pause