param([switch]$SkipBuild, [string]$NodePath)
$ErrorActionPreference='Stop'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -NodePath $NodePath }
$release=Join-Path $PSScriptRoot 'release'
$out=Join-Path $PSScriptRoot 'dist'
$null=[IO.Directory]::CreateDirectory($out)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$build=Get-Content -LiteralPath (Join-Path $release 'build-info.json') -Raw | ConvertFrom-Json
foreach($item in @(@('WeChatPet.exe','appSHA256'),@('WeChatObserver.exe','observerSHA256'),@('runtime/model-worker.mjs','workerSHA256'),@('runtime/node.exe','nodeSHA256'))) {
 if((Get-FileHash -LiteralPath (Join-Path $release $item[0]) -Algorithm SHA256).Hash -ne $build.($item[1])){throw ('Build file mismatch: '+$item[0])}
}
$token=[Guid]::NewGuid().ToString('N')
$zip=Join-Path $out ('payload-'+$token+'.zip')
$temporarySetup=Join-Path $out ('setup-'+$token+'.exe')
$setup=Join-Path $out 'WeChatPet-Setup-0.7.0.exe'
try {
 $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
 try {
  $files=@('WeChatPet.exe','WeChatObserver.exe','build-info.json','使用说明.md','runtime/node.exe','runtime/model-worker.mjs') | ForEach-Object {Get-Item -LiteralPath (Join-Path $release $_)}
  foreach($folder in @('assets','licenses')){$files+=Get-ChildItem -LiteralPath (Join-Path $release $folder) -File -Recurse}
  foreach($file in $files) {
   if(-not $file.FullName.StartsWith($release+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Payload outside release'}
   if(($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not packaged'}
   $parent=$file.Directory
   while($parent -and $parent.FullName -ne $release) {
    if(($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse points are not packaged'}
    $parent=$parent.Parent
   }
   $entry=$file.FullName.Substring($release.Length+1).Replace('\','/')
   [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal)
  }
 } finally { $archive.Dispose() }
 $compiler=Join-Path $env:SystemRoot 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
 & $compiler /nologo /target:winexe /platform:x64 ('/win32icon:'+(Join-Path $PSScriptRoot 'assets/wechat-pet/brand/wechat-pet.ico')) /reference:System.Windows.Forms.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll ('/resource:'+$zip+',payload.zip') ('/out:'+$temporarySetup) (Join-Path $PSScriptRoot 'Installer.cs')
 if($LASTEXITCODE -ne 0){throw '安装包构建失败'}
 Move-Item -LiteralPath $temporarySetup -Destination $setup -Force
} finally {
 foreach($path in @($zip,$temporarySetup)) {
  if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path -Force}
 }
}
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | Select-Object Path,Hash

