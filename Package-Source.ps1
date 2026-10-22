$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$out=Join-Path $PSScriptRoot 'dist'
$null=[IO.Directory]::CreateDirectory($out)
$destination=Join-Path $out 'WeChatPet-Source-0.7.0.zip'
$temporary=Join-Path $out ('source-'+[Guid]::NewGuid().ToString('N')+'.zip')
# Explicit entry points: do not export ad-hoc live-chat probes or diagnostics.
$rootFiles=@('Program.cs','PetWindow.cs','PetSettingsWindow.cs','PetVisuals.cs','SettingsDesignPreview.cs','Installer.cs','Build.ps1','Build-Icon.ps1','Package.ps1','Package-Source.ps1','README.md','LICENSE','SECURITY.md','ARCHITECTURE.md','.gitignore')
$testFiles=@('PetSelfTests.cs','AppearancePersistenceTests.cs','PetFullReplyTests.cs','RuntimeLifecycleTests.cs','Test-RuntimeLifecycle.ps1','SingleInstanceActivationTests.cs','MemoryWindowRefreshTests.cs','GroupMemoryPanelTests.cs','InstallerSafetyTests.cs','RuntimeSettingsPersistenceTests.cs','ReleaseBoundaryTests.cs','SettingsViewportTests.cs')
$files=@($rootFiles | ForEach-Object {Get-Item -LiteralPath (Join-Path $PSScriptRoot $_)})
$files+=@($testFiles | ForEach-Object {Get-Item -LiteralPath (Join-Path $PSScriptRoot ('tests/'+$_))})
$files+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Runtime') -File | Where-Object Extension -in @('.cs','.mjs')
$files+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests/Runtime') -File -Filter '*.cs'
$files+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'licenses') -File -Recurse
foreach($name in @('evidence.json','classic-mapping-evidence.json','newemoji-config.xml','emojipanel-config.xml','extract-classic.py')) {
 $files+=Get-Item -LiteralPath (Join-Path $PSScriptRoot ('tests/native-emoji-extraction/'+$name))
}
foreach($name in @('native-emoji-rea-ime-reset.json','native-emoji-ime-reset-25c15f0-provenance.json','native-emoji-paste-static-v2.json')) {
 $files+=Get-Item -LiteralPath (Join-Path $PSScriptRoot ('tests/'+$name))
}
foreach($skin in @('personal','whale','panda','brand','emoji')) {
 $files+=Get-ChildItem -LiteralPath (Join-Path $root ('assets/wechat-pet/'+$skin)) -File -Recurse
}
try {
 $archive=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach($file in ($files | Sort-Object FullName -Unique)) {
   if(-not $file.FullName.StartsWith($root+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source outside workspace'}
   if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not packaged'}
   $parent=$file.Directory
   while($parent -and $parent.FullName -ne $root) {
    if(($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not packaged'}
    $parent=$parent.Parent
   }
  }
  foreach($file in ($files | Sort-Object FullName -Unique)) {
   $name='wechat-ai-pet/'+$file.FullName.Substring($root.Length+1).Replace('\','/')
   [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$name,[IO.Compression.CompressionLevel]::Optimal)
  }
 } finally {$archive.Dispose()}
 Move-Item -LiteralPath $temporary -Destination $destination -Force
} finally {
 if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}
}
Get-FileHash -LiteralPath $destination -Algorithm SHA256 | Select-Object Path,Hash

