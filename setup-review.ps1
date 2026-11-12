# Explicit, review-only dependency setup. Does not launch the app or install tasks.
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$runtime=Join-Path $root 'runtime'
if (Test-Path -LiteralPath $runtime) { throw 'A runtime folder already exists. Review it manually; this script will not overwrite it.' }
$scratch=Join-Path $root 'work\setup'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$archive=Join-Path $scratch 'python-3.12.10-embed-amd64.zip'
Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip' -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $runtime
@('python312.zip','.','..','import site') | Set-Content -LiteralPath (Join-Path $runtime 'python312._pth') -Encoding ASCII
$bootstrap=Join-Path $scratch 'get-pip.py'
Invoke-WebRequest -Uri 'https://bootstrap.pypa.io/get-pip.py' -OutFile $bootstrap
$python=Join-Path $runtime 'python.exe'
& $python $bootstrap 'pip==26.3' --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw 'pip bootstrap failed.' }
& $python -m pip install -r (Join-Path $root 'requirements-lock.txt') --no-warn-script-location
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
Write-Output 'Dependencies prepared. The application and scheduler have not been started.'
