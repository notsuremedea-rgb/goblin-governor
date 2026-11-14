$ErrorActionPreference = 'Stop'
$run = Join-Path $PSScriptRoot ('.test-runs\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe = Join-Path $run 'GoblinGovernor.Tests.exe'
& $csc /nologo /target:exe /platform:x64 /main:Goblin.Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll "/out:$exe" "$PSScriptRoot\Gpu.cs" "$PSScriptRoot\Recovery.cs" "$PSScriptRoot\App.cs" "$PSScriptRoot\StartupPolicy.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& $exe | Tee-Object -FilePath (Join-Path $run 'test-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
