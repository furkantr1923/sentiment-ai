# Windows duman testleri: uygulama açılışı, geçici veri temizliği, Setup kurulumu/kaldırma, Portable açılışı.
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force reports | Out-Null
$R = 'reports/windows-tests.txt'
$script:pass = 0; $script:fail = 0
function Log($m) { $m | Tee-Object -FilePath $R -Append }
function Check($name, [bool]$ok) { if ($ok) { Log "[GEÇTİ] $name"; $script:pass++ } else { Log "[BAŞARISIZ] $name"; $script:fail++ } }
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

function Shot($file) {
  try {
    $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
    $bmp.Save("reports/$file"); $g.Dispose(); $bmp.Dispose()
  } catch { Log "Ekran görüntüsü alınamadı: $_" }
}

function WaitWindow($seconds) {
  for ($i = 0; $i -lt $seconds; $i++) {
    $p = Get-Process SentimentAI -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if ($p) { return $p }
    Start-Sleep 1
  }
  return $null
}

function CloseApp($p) {
  if (-not $p) { return }
  $null = $p.CloseMainWindow(); if (-not $p.WaitForExit(15000)) { $p.Kill() }
}

Log "SENTIMENT AI — Windows testleri ($([Environment]::OSVersion.VersionString)) — $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm')) UTC"
Check "SentimentAI.exe üretildi ($([math]::Round((Get-ChildItem out/win -Recurse | Measure-Object Length -Sum).Sum/1MB)) MB klasör)" (Test-Path out/win/SentimentAI.exe)
Check "SENTIMENT-AI-Setup.exe üretildi ($([math]::Round((Get-Item dist/SENTIMENT-AI-Setup.exe).Length/1MB)) MB)" (Test-Path dist/SENTIMENT-AI-Setup.exe)
Check "SENTIMENT-AI-Portable.exe üretildi ($([math]::Round((Get-Item dist/SENTIMENT-AI-Portable.exe).Length/1MB)) MB)" (Test-Path dist/SENTIMENT-AI-Portable.exe)

# 1) Klasör sürümü + geçici veri temizliği
$env:SENTIMENTAI_SELFTEST = '1'
$marker = Join-Path $env:TEMP 'sentimentai-selftest.txt'; Remove-Item $marker -ErrorAction SilentlyContinue
Start-Process out/win/SentimentAI.exe
$p = WaitWindow 45
Check "Uygulama açılıyor (pencere: '$($p.MainWindowTitle)')" ($null -ne $p)
Start-Sleep 4; Shot 'windows-ekran.png'
$cacheFile = if (Test-Path $marker) { Get-Content $marker -Raw } else { $null }
Check "Açılışta önbellekte geçici dosya oluştu (test işareti)" ($cacheFile -and (Test-Path $cacheFile.Trim()))
CloseApp $p; Start-Sleep 2
Check "Kapanışta geçici veriler temizlendi" ($cacheFile -and -not (Test-Path $cacheFile.Trim()))
$env:SENTIMENTAI_SELFTEST = $null

# 2) Setup.exe: sessiz kurulum, açılış, kaldırma
$dir = Join-Path $env:RUNNER_TEMP 'SentimentAI-Kurulum'
$s = Start-Process dist/SENTIMENT-AI-Setup.exe -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER',"/DIR=$dir" -Wait -PassThru
Check "Setup.exe kuruluyor (çıkış kodu $($s.ExitCode))" ($s.ExitCode -eq 0 -and (Test-Path "$dir/SentimentAI.exe"))
Start-Process "$dir/SentimentAI.exe"; $p = WaitWindow 45
Check "Kurulan uygulama açılıyor" ($null -ne $p); CloseApp $p
$u = Start-Process "$dir/unins000.exe" -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES' -Wait -PassThru; Start-Sleep 3
Check "Kaldırma çalışıyor" (-not (Test-Path "$dir/SentimentAI.exe"))

# 3) Portable.exe
Start-Process dist/SENTIMENT-AI-Portable.exe; $p = WaitWindow 120
Check "Portable EXE kurulumsuz açılıyor" ($null -ne $p)
Start-Sleep 3; Shot 'windows-portable.png'; CloseApp $p

Log "Toplam: geçti $script:pass, başarısız $script:fail"
Log "Not: GitHub Actions Windows Server sanal makinesinde otomatik testtir; analiz akışı arayüzden elle denenmemiştir."
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
