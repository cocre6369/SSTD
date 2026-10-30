param([Parameter(Mandatory)][string]$Path,[string]$Label,[int]$ChunkMB=2,[string]$Manifest = "artifacts/artifact-blobs.txt")
$ErrorActionPreference='Stop'
if(-not $env:GH_TOKEN){throw 'GH_TOKEN required.'}
if(-not $Label){$Label=[System.IO.Path]::GetFileName($Path)}
$bytes=[System.IO.File]::ReadAllBytes($Path)
$chunk=$ChunkMB*1MB
$count=[int][Math]::Ceiling($bytes.Length/$chunk)
for($i=0;$i -lt $count;$i++){
  $offset=$i*$chunk; $len=[Math]::Min($chunk,$bytes.Length-$offset)
  [byte[]]$part=[byte[]]::new($len); [Array]::Copy($bytes,$offset,$part,0,$len)
  $body=@{content=[Convert]::ToBase64String($part);encoding='base64'}|ConvertTo-Json -Compress
  try {
    $blob=$body | gh api "repos/$env:GITHUB_REPOSITORY/git/blobs" --method POST --input - | ConvertFrom-Json
    Write-Host "::notice title=ARTIFACT_BLOB::$Label $i $count $($bytes.Length) $($blob.sha)"
    New-Item -ItemType Directory -Force (Split-Path $Manifest) | Out-Null
    Add-Content $Manifest "$Label $i $count $($bytes.Length) $($blob.sha)"
  } catch {
    Write-Host "::error::ARTIFACT_BLOB_FAIL $Label $i $($_.Exception.Message)"
    throw
  }
  Start-Sleep -Milliseconds 250
}
