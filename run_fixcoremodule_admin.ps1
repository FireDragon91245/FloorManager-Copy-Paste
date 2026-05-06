$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSCommandPath
$propsPath = Join-Path $repoRoot 'Directory.Build.props.user'
if (-not (Test-Path $propsPath)) {
    throw 'Create Directory.Build.props.user with a GameDir property before running this script.'
}

[xml]$propsXml = Get-Content -Path $propsPath -Encoding UTF8
$gamePath = $propsXml.Project.PropertyGroup.GameDir
if ([string]::IsNullOrWhiteSpace($gamePath)) {
    throw 'Directory.Build.props.user is missing GameDir.'
}

$toolPath = Join-Path $repoRoot 'FixCoreModule_src\bin\Release\net8.0\FixCoreModule.dll'
$logPath = Join-Path $repoRoot 'fixcoremodule_admin_output.txt'

"[START] $(Get-Date -Format o)" | Set-Content -Path $logPath -Encoding UTF8
"GAME=$gamePath" | Add-Content -Path $logPath -Encoding UTF8
"TOOL=$toolPath" | Add-Content -Path $logPath -Encoding UTF8

Get-Process 'Data Center' -ErrorAction SilentlyContinue | Stop-Process -Force

& dotnet $toolPath --yes --path $gamePath *>&1 | ForEach-Object {
    $_.ToString()
} | Add-Content -Path $logPath -Encoding UTF8

$dll = Join-Path $gamePath 'MelonLoader\Il2CppAssemblies\UnityEngine.CoreModule.dll'
Get-ChildItem (Split-Path $dll) -Filter 'UnityEngine.CoreModule.dll*' |
    Select-Object Name, Length, Attributes, LastWriteTime |
    Format-Table -AutoSize |
    Out-String | Add-Content -Path $logPath -Encoding UTF8

"[END] $(Get-Date -Format o)" | Add-Content -Path $logPath -Encoding UTF8

