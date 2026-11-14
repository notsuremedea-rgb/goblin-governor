$ErrorActionPreference = 'Stop'
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll "/win32manifest:$PSScriptRoot\app.manifest" "/out:$PSScriptRoot\GoblinGovernor.exe" "$PSScriptRoot\Gpu.cs" "$PSScriptRoot\Recovery.cs" "$PSScriptRoot\App.cs" "$PSScriptRoot\StartupPolicy.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

