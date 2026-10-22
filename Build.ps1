param([string]$NodePath)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) { throw '需要 Windows x64 的 .NET Framework 4.x 编译器和 WPF。' }
$bin = Join-Path $PSScriptRoot 'release'
# Prefer the runtime already carried with the project; a clean source checkout
# can use an explicit executable or Node.js from PATH, regardless of its location.
$bundledNode = Join-Path $bin 'runtime/node.exe'
if (-not $NodePath) {
 if (Test-Path -LiteralPath $bundledNode) { $NodePath = $bundledNode }
 else { $nodeCommand = Get-Command node.exe -CommandType Application -ErrorAction SilentlyContinue; if ($nodeCommand) { $NodePath = $nodeCommand.Source } }
}
if (-not $NodePath -or -not (Test-Path -LiteralPath $NodePath -PathType Leaf)) { throw '未找到 Node.js v24.14.1。请安装到 PATH，或通过 -NodePath 指定 node.exe。' }
$NodePath = (Resolve-Path -LiteralPath $NodePath).Path
if ((& $NodePath --version) -ne 'v24.14.1' -or $LASTEXITCODE -ne 0) { throw '请使用已验证的 Node.js v24.14.1 构建，可通过 -NodePath 指定。' }
if ((& $NodePath -p 'process.arch') -ne 'x64' -or $LASTEXITCODE -ne 0) { throw '需要 x64 版本的 Node.js。' }
$null = [IO.Directory]::CreateDirectory($bin)
$references = @('WindowsBase','PresentationCore','PresentationFramework','UIAutomationClient','UIAutomationTypes') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\' + $_ + '.dll')) }
$references += @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll','/reference:System.Xml.Linq.dll','/reference:System.Xaml.dll','/reference:System.Security.dll','/reference:System.Core.dll')
$sources = @('Program.cs','PetWindow.cs','PetSettingsWindow.cs','PetVisuals.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
# Production assemblies must never contain test entry points or synthetic data.
$sources += Get-ChildItem (Join-Path $PSScriptRoot 'Runtime') -Filter '*.cs' | Where-Object Name -notlike '*Tests.cs' | ForEach-Object FullName
$icon = Join-Path $PSScriptRoot 'assets/wechat-pet/brand/wechat-pet.ico'
foreach ($target in @(@('WeChatPet.exe','WeChatPet.Program','winexe'),@('WeChatObserver.exe','WeChatPet.Runtime.ObserverProgram','exe'))) {
 & (Join-Path $framework 'csc.exe') /nologo ('/target:'+$target[2]) /platform:x64 ('/win32icon:'+$icon) ('/main:'+$target[1]) ('/out:'+(Join-Path $bin $target[0])) @references @sources
 if ($LASTEXITCODE -ne 0) { throw ('构建失败：'+$target[0]) }
}
$runtime = Join-Path $bin 'runtime'
$null = [IO.Directory]::CreateDirectory($runtime)
Copy-Item (Join-Path $PSScriptRoot 'Runtime/model-worker.mjs') $runtime -Force
if (-not [String]::Equals([IO.Path]::GetFullPath($NodePath),[IO.Path]::GetFullPath($bundledNode),[StringComparison]::OrdinalIgnoreCase)) { Copy-Item -LiteralPath $NodePath -Destination $bundledNode -Force }
$assets = Join-Path $bin 'assets/wechat-pet'
$null = [IO.Directory]::CreateDirectory($assets)
Copy-Item (Join-Path $PSScriptRoot 'assets/wechat-pet/personal') $assets -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'assets/wechat-pet/whale') $assets -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'assets/wechat-pet/panda') $assets -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'assets/wechat-pet/brand') $assets -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'assets/wechat-pet/emoji') $assets -Recurse -Force
if (Test-Path (Join-Path $PSScriptRoot 'licenses')) { Copy-Item (Join-Path $PSScriptRoot 'licenses') $bin -Recurse -Force }
Copy-Item (Join-Path $PSScriptRoot 'docs') $bin -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'LICENSE') $bin -Force
Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $bin '使用说明.md') -Force
@{ version='0.7.1'; builtAtUtc=[DateTime]::UtcNow.ToString('o'); appSHA256=(Get-FileHash (Join-Path $bin 'WeChatPet.exe') -Algorithm SHA256).Hash; observerSHA256=(Get-FileHash (Join-Path $bin 'WeChatObserver.exe') -Algorithm SHA256).Hash; workerSHA256=(Get-FileHash (Join-Path $runtime 'model-worker.mjs') -Algorithm SHA256).Hash; nodeVersion='24.14.1'; nodeSHA256=(Get-FileHash (Join-Path $runtime 'node.exe') -Algorithm SHA256).Hash; supportedWeChat='4.1.15.13'; ocr=$false; automaticSending=$false } | ConvertTo-Json | Set-Content (Join-Path $bin 'build-info.json') -Encoding UTF8
Write-Output (Join-Path $bin 'WeChatPet.exe')
