$ErrorActionPreference = 'SilentlyContinue'
$roots = @(
  'C:\Windows',
  'C:\Users',
  'C:\Program Files',
  'C:\Program Files (x86)',
  'C:\ProgramData',
  'C:\Drivers',
  'C:\Apps'
)

$bucketTotals = @{}
$bigFiles = New-Object System.Collections.Generic.List[object]

foreach ($root in $roots) {
  if (-not (Test-Path $root)) { continue }
  Get-ChildItem -LiteralPath $root -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object {
    $len = $_.Length
    if ($len -gt 300MB) {
      $bigFiles.Add([PSCustomObject]@{ Path = $_.FullName; MB = [math]::Round($len/1MB,1) })
    }
    $rel = $_.FullName.Substring($root.Length).TrimStart('\')
    $parts = $rel -split '\\'
    if ($parts.Length -ge 2) {
      $bucket = Join-Path $root ($parts[0] + '\' + $parts[1])
    } elseif ($parts.Length -eq 1) {
      $bucket = Join-Path $root $parts[0]
    } else {
      $bucket = $root
    }
    if ($bucketTotals.ContainsKey($bucket)) {
      $bucketTotals[$bucket] += $len
    } else {
      $bucketTotals[$bucket] = $len
    }
  }
  "DONE ROOT: $root"
}

"=== TOP 40 FOLDER BUCKETS (depth 2) ==="
$bucketTotals.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 40 | ForEach-Object {
  "{0,10:N2} GB  {1}" -f ($_.Value/1GB), $_.Key
}

"=== FILES OVER 300MB ==="
$bigFiles | Sort-Object MB -Descending | ForEach-Object {
  "{0,10:N1} MB  {1}" -f $_.MB, $_.Path
}

"=== DONE ==="
