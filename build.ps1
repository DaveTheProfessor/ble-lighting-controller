# Compile source only. Does not run the application or modify scheduled tasks.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler was not found.' }
& $compiler /nologo /codepage:65001 /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (('/win32icon:')+(Join-Path $root 'assets\led-sign-studio-taskbar.ico')) (('/out:')+(Join-Path $root 'LED Sign Studio.exe')) (Join-Path $root 'StudioApp.cs')
if ($LASTEXITCODE -ne 0) { throw 'Studio compilation failed.' }
& $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll (('/out:')+(Join-Path $root 'LED Sign Controller.exe')) (Join-Path $root 'ControllerApp.cs')
if ($LASTEXITCODE -ne 0) { throw 'Legacy controller compilation failed.' }
Write-Output 'Executables compiled. Nothing has been launched or deployed.'
