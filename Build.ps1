param([switch]$Test, [switch]$Preview, [string]$NodePath)
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
$testSources = @($sources) + @(Get-ChildItem (Join-Path $PSScriptRoot 'tests/Runtime') -Filter '*.cs' | ForEach-Object FullName) + @(Join-Path $PSScriptRoot 'tests/PetSelfTests.cs')
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
Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $bin '使用说明.md') -Force
@{ version='0.7.0'; builtAtUtc=[DateTime]::UtcNow.ToString('o'); appSHA256=(Get-FileHash (Join-Path $bin 'WeChatPet.exe') -Algorithm SHA256).Hash; observerSHA256=(Get-FileHash (Join-Path $bin 'WeChatObserver.exe') -Algorithm SHA256).Hash; workerSHA256=(Get-FileHash (Join-Path $runtime 'model-worker.mjs') -Algorithm SHA256).Hash; nodeVersion='24.14.1'; nodeSHA256=(Get-FileHash (Join-Path $runtime 'node.exe') -Algorithm SHA256).Hash; supportedWeChat='4.1.15.13'; ocr=$false; automaticSending=$false } | ConvertTo-Json | Set-Content (Join-Path $bin 'build-info.json') -Encoding UTF8
$testArtifacts = @('RuntimeTests.exe','WeChatPetTests.exe','PetFullReplyTests.exe','AppearancePersistenceTests.exe','SingleInstanceActivationTests.exe','MemoryWindowRefreshTests.exe','GroupMemoryPanelTests.exe','InstallerSafetyTests.exe','RuntimeSettingsPersistenceTests.exe','ReleaseBoundaryTests.exe','SettingsViewportTests.exe','SettingsDesignPreview.exe')
try {
if ($Test) {
 $runtimeTest = Join-Path $bin 'RuntimeTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:WeChatPet.Runtime.RuntimeTests ('/out:'+$runtimeTest) @references @testSources
 if ($LASTEXITCODE -ne 0) { throw '运行状态自检构建失败。' }
 $testExe = Join-Path $bin 'WeChatPetTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:WeChatPet.PetTestMain ('/out:'+$testExe) @references @testSources
 if ($LASTEXITCODE -ne 0) { throw '自检程序构建失败。' }
 & $testExe
 if ($LASTEXITCODE -ne 0) { throw '外观自检失败。' }
 $fullReplyTest=Join-Path $bin 'PetFullReplyTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:PetFullReplyTests ('/out:'+$fullReplyTest) @references @sources (Join-Path $PSScriptRoot 'tests/PetFullReplyTests.cs')
 if($LASTEXITCODE -ne 0){throw '宠物全文预览自检构建失败。'}
 & $fullReplyTest
 if($LASTEXITCODE -ne 0){throw '宠物全文预览自检失败。'}
 $appearanceTest=Join-Path $bin 'AppearancePersistenceTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:AppearancePersistenceTests ('/out:'+$appearanceTest) @references @sources (Join-Path $PSScriptRoot 'tests/AppearancePersistenceTests.cs')
 if($LASTEXITCODE -ne 0){throw '外观保存自检构建失败。'}
 & $appearanceTest
 if($LASTEXITCODE -ne 0){throw '外观保存自检失败。'}
 & (Join-Path $bin 'RuntimeTests.exe')
 if ($LASTEXITCODE -ne 0) { throw '运行状态自检失败。' }
 & (Join-Path $PSScriptRoot 'tests/Test-RuntimeLifecycle.ps1')
 $activationTest=Join-Path $bin 'SingleInstanceActivationTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$activationTest) /reference:System.Core.dll /reference:System.Security.dll (Join-Path $PSScriptRoot 'Runtime/SingleInstanceActivation.cs') (Join-Path $PSScriptRoot 'tests/SingleInstanceActivationTests.cs')
 if($LASTEXITCODE -ne 0){throw '单实例自检构建失败。'}
 & $activationTest
 if($LASTEXITCODE -ne 0){throw '单实例自检失败。'}
 $memoryUiTest=Join-Path $bin 'MemoryWindowRefreshTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$memoryUiTest) ('/reference:'+(Join-Path $bin 'WeChatPet.exe')) @references (Join-Path $PSScriptRoot 'tests/MemoryWindowRefreshTests.cs')
 if($LASTEXITCODE -ne 0){throw '记忆界面自检构建失败。'}
 & $memoryUiTest
 if($LASTEXITCODE -ne 0){throw '记忆界面自检失败。'}
 $groupMemoryUiTest=Join-Path $bin 'GroupMemoryPanelTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$groupMemoryUiTest) ('/reference:'+(Join-Path $bin 'WeChatPet.exe')) @references (Join-Path $PSScriptRoot 'tests/GroupMemoryPanelTests.cs')
 if($LASTEXITCODE -ne 0){throw '群聊记忆界面自检构建失败。'}
 & $groupMemoryUiTest
 if($LASTEXITCODE -ne 0){throw '群聊记忆界面自检失败。'}
 $viewportTest=Join-Path $bin 'SettingsViewportTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:SettingsViewportTests ('/out:'+$viewportTest) @references @sources (Join-Path $PSScriptRoot 'tests/SettingsViewportTests.cs')
 if($LASTEXITCODE -ne 0){throw '设置视口自检构建失败。'}
 & $viewportTest
 if($LASTEXITCODE -ne 0){throw '设置视口自检失败。'}
 $installerTest=Join-Path $bin 'InstallerSafetyTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:InstallerSafetyTests ('/out:'+$installerTest) /reference:System.Windows.Forms.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $PSScriptRoot 'Installer.cs') (Join-Path $PSScriptRoot 'tests/InstallerSafetyTests.cs')
 if($LASTEXITCODE -ne 0){throw '安装器自检构建失败。'}
 & $installerTest
 if($LASTEXITCODE -ne 0){throw '安装器自检失败。'}
 $settingsTest=Join-Path $bin 'RuntimeSettingsPersistenceTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$settingsTest) /reference:System.Core.dll /reference:System.Security.dll /reference:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'Runtime/ModelClient.cs') (Join-Path $PSScriptRoot 'Runtime/Contracts.cs') (Join-Path $PSScriptRoot 'Runtime/MemoryIdentity.cs') (Join-Path $PSScriptRoot 'Runtime/RuntimeSettingsPersistence.cs') (Join-Path $PSScriptRoot 'tests/RuntimeSettingsPersistenceTests.cs')
 if($LASTEXITCODE -ne 0){throw '设置持久化自检构建失败。'}
 & $settingsTest
 if($LASTEXITCODE -ne 0){throw '设置持久化自检失败。'}
 & (Join-Path $runtime 'node.exe') --test (Join-Path $PSScriptRoot 'Runtime/model-worker.test.mjs')
 if ($LASTEXITCODE -ne 0) { throw '模型协议自检失败。' }
 & (Join-Path $runtime 'node.exe') --test (Join-Path $PSScriptRoot 'Runtime/model-worker.integration.test.mjs')
 if ($LASTEXITCODE -ne 0) { throw '模型失败恢复自检失败。' }
 $boundaryTest=Join-Path $bin 'ReleaseBoundaryTests.exe'
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 ('/out:'+$boundaryTest) /reference:System.Core.dll (Join-Path $PSScriptRoot 'tests/ReleaseBoundaryTests.cs')
 if($LASTEXITCODE -ne 0){throw '发布边界自检构建失败。'}
 & $boundaryTest
 if($LASTEXITCODE -ne 0){throw '发布边界自检失败。'}
}
if ($Preview) {
 & (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /main:WeChatPet.SettingsDesignPreview ('/out:'+(Join-Path $bin 'SettingsDesignPreview.exe')) @references @sources (Join-Path $PSScriptRoot 'SettingsDesignPreview.cs')
 if ($LASTEXITCODE -ne 0) { throw '外观检查构建失败。' }
 & (Join-Path $bin 'SettingsDesignPreview.exe')
 if ($LASTEXITCODE -ne 0) { throw '外观预览渲染失败。' }
}
} finally {
 # These named outputs are owned by this build; keep release usable as a clean
 # application directory even after a failed test or render.
 foreach($name in $testArtifacts) {
  $path=Join-Path $bin $name
  if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
 }
}
Write-Output (Join-Path $bin 'WeChatPet.exe')

