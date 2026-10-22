$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=[IO.Path]::GetFullPath($PSScriptRoot)
$out=Join-Path $PSScriptRoot 'dist'
$null=[IO.Directory]::CreateDirectory($out)
$destination=Join-Path $out 'WeChatPet-Source-0.7.1.zip'
$temporary=Join-Path $out ('source-'+[Guid]::NewGuid().ToString('N')+'.zip')
# Explicit entry points: do not export ad-hoc live-chat probes or diagnostics.
$rootFiles=@('Program.cs','PetWindow.cs','PetSettingsWindow.cs','PetVisuals.cs','Installer.cs','Build.ps1','Build-Icon.ps1','Package.ps1','Package-Source.ps1','README.md','LICENSE','.gitignore')
$files=@($rootFiles | ForEach-Object {Get-Item -LiteralPath (Join-Path $PSScriptRoot $_)})
$files+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Runtime') -File | Where-Object Extension -in @('.cs','.mjs')
foreach($folder in @('assets','licenses','docs')) {
 $files+=Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse
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

