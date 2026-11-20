param(
  [string]$BaseUrl = "http://localhost:11434",
  [string]$Model = "llama3:8b",
  [int]$Requests = 5,
  [string]$Prompt = "Summarize: safety checks for a pump start procedure."
)

$ErrorActionPreference = "Stop"

function Invoke-Generate {
  param([string]$Url, [string]$ModelName, [string]$PromptText)
  $body = @{ model = $ModelName; prompt = $PromptText; stream = $false } | ConvertTo-Json
  $headers = @{ "Content-Type" = "application/json" }
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  Invoke-RestMethod -Method Post -Uri "$Url/api/generate" -Body $body -Headers $headers | Out-Null
  $sw.Stop()
  return $sw.Elapsed.TotalMilliseconds
}

$durations = @()
for ($i = 0; $i -lt $Requests; $i++) {
  $durations += Invoke-Generate -Url $BaseUrl -ModelName $Model -PromptText $Prompt
}

$sorted = $durations | Sort-Object
$p95Index = [Math]::Ceiling($sorted.Count * 0.95) - 1
$p95 = $sorted[[Math]::Max($p95Index, 0)]
$avg = ($durations | Measure-Object -Average).Average

Write-Host "Requests: $Requests"
Write-Host "Average ms: {0:N1}" -f $avg
Write-Host "P95 ms: {0:N1}" -f $p95
