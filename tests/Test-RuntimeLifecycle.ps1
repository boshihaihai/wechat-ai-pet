param([string]$ResultPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
$framework=Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319'
$output=Join-Path $PSScriptRoot ('RuntimeLifecycleTests.'+[Guid]::NewGuid().ToString('N')+'.exe')
try {
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:RuntimeLifecycleTests ('/out:'+$output) /reference:System.Core.dll /reference:System.Web.Extensions.dll (Join-Path $project 'Runtime/ObserverShutdown.cs') (Join-Path $project 'Runtime/GuardianHeartbeat.cs') (Join-Path $PSScriptRoot 'RuntimeLifecycleTests.cs')
 if($LASTEXITCODE -ne 0){throw '生命周期测试编译失败'}
 $result=& $output
 $exitCode=$LASTEXITCODE
 if($ResultPath){$result | Set-Content -LiteralPath $ResultPath -Encoding UTF8}
 $result
 if($exitCode -ne 0){throw '生命周期测试失败'}
} finally {
 if(Test-Path -LiteralPath $output){Remove-Item -LiteralPath $output -Force}
}
