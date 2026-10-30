param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference='Stop'
if(-not(Test-Path $Path)){return}
$content=[Convert]::ToBase64String([System.IO.File]::ReadAllBytes($Path))
$body=@{content=$content;encoding='base64'} | ConvertTo-Json -Compress
$blob=gh api "repos/$env:GITHUB_REPOSITORY/git/blobs" --method POST --input - <<< $body | ConvertFrom-Json
Write-Host "::notice title=CI_LOG::$Path $($blob.sha)"
