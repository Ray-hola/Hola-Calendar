# End-to-end check against the real server.py: boots a throwaway server on a
# temp database, signs in with a seeded account through holactl (the same core
# the app uses), lists today's to-dos, completes one, then confirms the
# server's refusals (409 on a repeat, 403 for the admin account) come back as
# readable messages. Credential Manager is not read (tokens via HOLA_TOKEN).
#   scripts/integration-test.ps1   (expects scripts/build.ps1 to have produced build\holactl.exe)
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
$Repo = Split-Path -Parent (Split-Path -Parent $Root)
$Ctl = Join-Path $Root 'build\holactl.exe'
if (-not (Test-Path $Ctl)) { Write-Host 'build first: scripts/build.ps1'; exit 1 }
# Loopback must not go through a proxy some shells inject.
foreach ($v in 'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY', 'http_proxy', 'https_proxy', 'all_proxy') { Remove-Item "env:$v" -ErrorAction SilentlyContinue }
$Port = if ($env:PORT) { $env:PORT } else { '48173' }
$Work = Join-Path ([IO.Path]::GetTempPath()) ('hola-it-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $Work | Out-Null
$python = if (Get-Command python -ErrorAction SilentlyContinue) { 'python' } else { 'py' }
$server = Start-Process $python -ArgumentList "`"$Repo\server.py`" --port $Port --db `"$Work\db.sqlite3`"" -PassThru -WindowStyle Hidden `
  -RedirectStandardOutput "$Work\server.log" -RedirectStandardError "$Work\server.err"
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$fails = 0
try {
  $up = $false
  for ($i = 0; $i -lt 50 -and -not $up -and -not $server.HasExited; $i++) {
    try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/api/health" -TimeoutSec 3 | Out-Null; $up = $true } catch { Start-Sleep -Milliseconds 200 }
  }
  if (-not $up) { Get-Content "$Work\server.err"; Write-Host 'server did not start'; exit 1 }
  $accounts = @{}
  Get-Content "$Work\initial-accounts.txt" -Encoding UTF8 | ForEach-Object { $f = $_ -split "`t"; if ($f.Count -ge 2) { $accounts[$f[0]] = $f[1] } }
  foreach ($u in 'test001', 'superadmin') { if (-not $accounts[$u]) { Write-Host "no seeded password for $u"; exit 1 } }
  $url = "127.0.0.1:$Port"

  function Ctl([string]$token, [string]$password) {
    $ErrorActionPreference = 'Continue'   # holactl's own errors arrive on stderr
    # An empty value would unset the variable and send holactl to Credential Manager or a password prompt.
    $env:HOLA_TOKEN = if ($token) { $token } else { '-' }; $env:HOLA_PASSWORD = if ($password) { $password } else { '-' }
    $out = $null | & $Ctl @args 2>&1 | ForEach-Object { "$_" }
    Remove-Item env:HOLA_TOKEN, env:HOLA_PASSWORD -ErrorAction SilentlyContinue
    return ($out -join "`n")
  }
  function Expect([string]$what, [string]$out, [string]$want) {
    if ($out.Contains($want)) { Write-Host "  ok   $what" }
    else { Write-Host "  FAIL $what`: expected «$want» in:"; $out -split "`n" | ForEach-Object { Write-Host "       $_" }; $script:fails++ }
  }
  function FirstOpen([string]$out) { foreach ($l in $out -split "`n") { if ($l -match '^\[ \] (\S+)') { return $Matches[1] } } }

  Write-Host '→ login as test001 (seeded member)'
  $token = (Ctl '' $accounts['test001'] login $url test001 --print-token).Trim()
  if ($token.Length -gt 0 -and -not $token.Contains(' ')) { Write-Host "  ok   got a session token ($($token.Length) chars)" } else { Write-Host "  FAIL no token: $token"; $fails++ }

  Write-Host '→ wrong password is refused with the server''s text'
  Expect '401 message' (Ctl '' 'nope' login $url test001 --print-token) '账户或密码错误'

  Write-Host '→ today''s list'
  $out = Ctl $token '' today $url
  $out -split "`n" | ForEach-Object { Write-Host "       $_" }
  Expect 'seeded P0 task listed' $out '核验周轴与悬停'
  Expect 'open marker' $out '[ ]'
  $task = FirstOpen $out
  if (-not $task) { Write-Host 'no runnable fixture task'; exit 1 }

  Write-Host "→ complete $task"
  Expect 'completed' (Ctl $token '' complete $url $task) 'completed'
  Expect 'now ticked' (Ctl $token '' today $url) "[x] $task"

  Write-Host '→ completing it again is refused (409)'
  Expect '409 message' (Ctl $token '' complete $url $task) '本次任务已记录'

  Write-Host '→ admin account cannot tick (403)'
  $admin = (Ctl '' $accounts['superadmin'] login $url superadmin --print-token).Trim()
  $out = Ctl $admin '' today $url
  Expect 'admin sees everyone''s tasks' $out '测试任务完成与递延'
  $other = FirstOpen $out
  if (-not $other) { Write-Host 'no runnable admin fixture task'; exit 1 }
  Expect '403 message' (Ctl $admin '' complete $url $other) 'superadmin 账号仅用于'

  Write-Host '→ a stale token is unauthorized'
  Expect '401 on stale' (Ctl 'stale' '' today $url) '请登录工作台'

  Write-Host '→ logout invalidates the token'
  Ctl $token '' logout $url | Out-Null
  Expect 'token dead after logout' (Ctl $token '' today $url) '请登录工作台'

  Write-Host '→ offline server'
  Expect 'offline message' (Ctl 'x' '' today '127.0.0.1:1') 'error:'
}
finally {
  # The py launcher starts python.exe as a child, so the whole tree goes.
  $ErrorActionPreference = 'Continue'
  if (-not $server.HasExited) { & taskkill /T /F /PID $server.Id 2>&1 | Out-Null }
  Start-Sleep -Milliseconds 300
  Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
}
if ($fails -eq 0) { Write-Host 'integration: all checks passed' } else { Write-Host "integration: $fails check(s) failed"; exit 1 }
