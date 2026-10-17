# End-to-end test of the SILENT installer path WITHOUT administrator rights and without touching the real machine.
# Needs a ShabpaSetup.exe built with test hooks and a payload, e.g.
#   dotnet build src/Setup/Setup.csproj -c Release -p:DefineConstants=SHABPA_TESTHOOKS -p:PayloadFile=<payload.bin> -o <dir>
# Usage: powershell -File silent-install-harness.ps1 -Exe <dir>\ShabpaSetup.exe [-NoPayloadExe <exe built without payload>]
# Every case runs with __COMPAT_LAYER=RunAsInvoker (no UAC) and SHABPA_SETUP_ROOT (temp folder + HKCU test key).
param([Parameter(Mandatory)][string]$Exe, [string]$NoPayloadExe)
$ErrorActionPreference = 'Stop'
Add-Type @'
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class Win {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  public static List<string> Windows(uint pid) {
    var r = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) { var t = new StringBuilder(256); var c = new StringBuilder(256); GetWindowText(h, t, 256); GetClassName(h, c, 256); r.Add((IsWindowVisible(h) ? "VISIBLE " : "hidden ") + c + " '" + t + "'"); } return true; }, IntPtr.Zero);
    return r;
  }
}
'@
$work = Join-Path $env:TEMP ('shabpa-harness-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory $work | Out-Null
$script:fail = 0
$script:results = @()

function Run-Case {
  param([string]$Name, [string]$Root, [string]$CmdArgs, [hashtable]$Env = @{}, [switch]$MinimalEnv, [switch]$Redirect, [string]$ExePath = $Exe, [int]$TimeoutSec = 60)
  $log = (Join-Path $work ($Name -replace '[^\w]', '_')) + '.log'
  $psi = New-Object Diagnostics.ProcessStartInfo $ExePath
  $psi.Arguments = $CmdArgs
  $psi.UseShellExecute = $false
  if ($Redirect) { $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true }
  if ($MinimalEnv) {
    $keep = @{ SystemRoot = $env:SystemRoot; windir = $env:windir; ComSpec = $env:ComSpec }
    $psi.EnvironmentVariables.Clear()
    foreach ($k in $keep.Keys) { $psi.EnvironmentVariables[$k] = $keep[$k] }
  }
  $psi.EnvironmentVariables['__COMPAT_LAYER'] = 'RunAsInvoker'
  $psi.EnvironmentVariables['SHABPA_SETUP_ROOT'] = $Root
  $psi.EnvironmentVariables['SHABPA_SETUP_LOG'] = $log
  if (-not $Env.ContainsKey('SHABPA_SETUP_RUNTIME')) { $psi.EnvironmentVariables['SHABPA_SETUP_RUNTIME'] = 'present' }
  foreach ($k in $Env.Keys) { $psi.EnvironmentVariables[$k] = [string]$Env[$k] }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = [Diagnostics.Process]::Start($psi)
  $seen = New-Object System.Collections.Generic.HashSet[string]
  while (-not $p.HasExited -and $sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
    foreach ($w in [Win]::Windows([uint32]$p.Id)) { [void]$seen.Add($w) }
    Start-Sleep -Milliseconds 20
  }
  $hung = -not $p.HasExited
  if ($hung) { $p.Kill(); $p.WaitForExit() }
  $ms = [int]$sw.Elapsed.TotalMilliseconds
  $gone = $p.HasExited
  $code = $p.ExitCode
  $logText = if (Test-Path $log) { Get-Content $log -Raw -Encoding UTF8 } else { '(no log)' }
  [pscustomobject]@{ Name = $Name; Code = $code; Ms = $ms; Hung = $hung; Gone = $gone; Windows = @($seen); Log = $logText }
}

function Check {
  param($Result, [int]$ExpectCode, [int]$MaxMs = 15000, [scriptblock]$Extra)
  $problems = @()
  if ($Result.Code -ne $ExpectCode) { $problems += "exit $($Result.Code) expected $ExpectCode" }
  if ($Result.Hung) { $problems += 'HUNG (killed)' }
  if (-not $Result.Gone) { $problems += 'process still alive after exit' }
  if ($Result.Ms -gt $MaxMs) { $problems += "took $($Result.Ms) ms > $MaxMs" }
  if ($Result.Windows.Count -gt 0) { $problems += 'windows seen: ' + ($Result.Windows -join '; ') }
  if ($Extra) { $problems += @(& $Extra) }
  $status = if ($problems.Count) { 'FAIL' } else { 'ok  ' }
  if ($problems.Count) { $script:fail++ }
  $line = "{0} {1,-34} exit={2,-3} {3,6} ms  {4}" -f $status, $Result.Name, $Result.Code, $Result.Ms, ($problems -join ' | ')
  Write-Host $line
  $script:results += $line
  if ($problems.Count) { Write-Host $Result.Log }
}

function NewRoot($n) { $r = Join-Path $work $n; New-Item -ItemType Directory $r | Out-Null; $r }
function TestKey($root) { $h = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\ShabpaSetupTest'); $names = if ($h) { $h.GetSubKeyNames() } else { @() }; $h }

# 1 happy path, Desktop and Start Menu folders do not exist
$r = NewRoot 'happy'
$res = Run-Case 'happy path' $r '--quiet'
Check $res 0 -Extra {
  $e = @()
  foreach ($f in 'Program Files\Shabpa\Shabpa.exe', 'Program Files\Shabpa\Shabpa.dll', 'Program Files\Shabpa\amd64\native.dll', 'Program Files\Shabpa\uninstall.exe', 'Program Files\Shabpa\install.manifest', 'Start\Shabpa.lnk', 'Desktop\Shabpa.lnk') {
    if (-not (Test-Path (Join-Path $r $f))) { $e += "missing $f" }
  }
  $k = Get-ChildItem 'HKCU:\Software\ShabpaSetupTest' | ForEach-Object { Get-ItemProperty (Join-Path $_.PSPath 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Shabpa') -ErrorAction SilentlyContinue } | Select-Object -Last 1
  foreach ($n in 'DisplayName', 'DisplayVersion', 'Publisher', 'InstallLocation', 'UninstallString', 'QuietUninstallString', 'DisplayIcon', 'EstimatedSize') {
    if (-not $k.$n) { $e += "registry value $n missing" }
  }
  if ($k.DisplayVersion -ne '3.5.1') { $e += "DisplayVersion=$($k.DisplayVersion)" }
  $e
}
$happyLog = $res.Log

# 2 same again (update / idempotent), --update keeps the existing desktop link
Check (Run-Case 'second run (update)' $r '--quiet --update') 0

# 3 argument spellings
$i = 0
foreach ($a in '--QUIET', '/quiet', '-q', '/S', '--quiet --no-desktop', '--silent', '/verysilent') {
  $i++
  Check (Run-Case "args $a" (NewRoot "args$i") $a) 0
}

# 4 Start Menu location is a FILE (shortcut cannot be created) -> install still succeeds
$r = NewRoot 'startfile'; Set-Content (Join-Path $r 'Start') 'x'
Check (Run-Case 'start menu unusable' $r '--quiet') 0

# 5 program folder blocked by a file -> exit 11
$r = NewRoot 'blocked'; New-Item -ItemType Directory (Join-Path $r 'Program Files') | Out-Null; Set-Content (Join-Path $r 'Program Files\Shabpa') 'x'
Check (Run-Case 'program dir blocked (11)' $r '--quiet') 11

# 6 runtime missing + connection refused -> exit 10, fast
Check (Run-Case 'runtime missing, refused (10)' (NewRoot 'rt1') '--quiet' @{ SHABPA_SETUP_RUNTIME = 'missing'; SHABPA_SETUP_RUNTIME_URL = 'http://127.0.0.1:9/dotnet.exe' }) 10 -MaxMs 10000

# 7 runtime missing + server that never answers -> exit 10 after the header timeout (3 s here)
$listener = New-Object Net.HttpListener; $listener.Prefixes.Add('http://127.0.0.1:18765/'); $listener.Start()
$pending = $listener.GetContextAsync()
Check (Run-Case 'runtime missing, silent server (10)' (NewRoot 'rt2') '--quiet' @{ SHABPA_SETUP_RUNTIME = 'missing'; SHABPA_SETUP_RUNTIME_URL = 'http://127.0.0.1:18765/x.exe'; SHABPA_SETUP_HTTP_TIMEOUT_SEC = 3 }) 10 -MaxMs 12000
# 8 watchdog: same server, header timeout long, watchdog 3 s -> exit 15
Check (Run-Case 'watchdog (15)' (NewRoot 'wd') '--quiet' @{ SHABPA_SETUP_RUNTIME = 'missing'; SHABPA_SETUP_RUNTIME_URL = 'http://127.0.0.1:18765/y.exe'; SHABPA_SETUP_HTTP_TIMEOUT_SEC = 120; SHABPA_SETUP_WATCHDOG_SEC = 3 }) 15 -MaxMs 12000
$listener.Stop()

# 9 runtime missing + a download that is not Microsoft-signed -> exit 10, never run
Set-Content (Join-Path $work 'unsigned.exe') 'MZ not a real program'
$job = Start-Job -ArgumentList $work -ScriptBlock { param($w) $l = New-Object Net.HttpListener; $l.Prefixes.Add('http://127.0.0.1:18767/'); $l.Start(); $c = $l.GetContext(); $b = [IO.File]::ReadAllBytes("$w\unsigned.exe"); $c.Response.ContentLength64 = $b.Length; $c.Response.OutputStream.Write($b, 0, $b.Length); $c.Response.Close(); $l.Stop() }
Start-Sleep -Milliseconds 1500
Check (Run-Case 'runtime unsigned download (10)' (NewRoot 'rt3') '--quiet' @{ SHABPA_SETUP_RUNTIME = 'missing'; SHABPA_SETUP_RUNTIME_URL = 'http://127.0.0.1:18767/r.exe' }) 10 -MaxMs 15000
Receive-Job $job -Wait -ErrorAction SilentlyContinue | Out-Null; Remove-Job $job -Force -ErrorAction SilentlyContinue

# 9b download that starts and then stalls mid-body -> exit 10 after the stall limit (3 s here)
$job2 = Start-Job -ScriptBlock { $l = New-Object Net.HttpListener; $l.Prefixes.Add('http://127.0.0.1:18768/'); $l.Start(); $c = $l.GetContext(); $c.Response.ContentLength64 = 50000000; $b = New-Object byte[] 1000; $c.Response.OutputStream.Write($b, 0, 1000); $c.Response.OutputStream.Flush(); Start-Sleep -Seconds 40; $l.Stop() }
Start-Sleep -Milliseconds 1500
Check (Run-Case 'runtime download stalls (10)' (NewRoot 'rt4') '--quiet' @{ SHABPA_SETUP_RUNTIME = 'missing'; SHABPA_SETUP_RUNTIME_URL = 'http://127.0.0.1:18768/s.exe'; SHABPA_SETUP_HTTP_TIMEOUT_SEC = 3 }) 10 -MaxMs 12000
Stop-Job $job2 -ErrorAction SilentlyContinue; Remove-Job $job2 -Force -ErrorAction SilentlyContinue

# 9c default log location (no override): %ProgramData%\Shabpa\setup-log.txt is created; removed again afterwards
$pdLog = Join-Path $env:ProgramData 'Shabpa\setup-log.txt'; $pdExisted = Test-Path $pdLog
$res = Run-Case 'default log location' (NewRoot 'deflog') '--quiet' @{ SHABPA_SETUP_LOG = '' }
Check $res 0 -Extra { if (-not (Test-Path $pdLog)) { 'no log in ProgramData' } elseif (-not (Select-String -Path $pdLog -Pattern 'install finished' -Quiet)) { 'log lacks the run' } }
if (-not $pdExisted) { Remove-Item $pdLog -Force -ErrorAction SilentlyContinue; Remove-Item (Join-Path $env:ProgramData 'Shabpa') -ErrorAction SilentlyContinue }
# 10 redirected stdin/stdout/stderr
Check (Run-Case 'stdio redirected' (NewRoot 'redir') '--quiet' -Redirect) 0

# 11 minimal environment (no TEMP, USERPROFILE, ProgramData, PATH ...)
Check (Run-Case 'minimal environment' (NewRoot 'minenv') '--quiet' -MinimalEnv) 0

# 12 uninstall
$r = NewRoot 'uninst'
Check (Run-Case 'install for uninstall' $r '--quiet') 0
Check (Run-Case 'uninstall' $r '--uninstall --quiet') 0 -Extra {
  $e = @()
  Start-Sleep -Seconds 4
  if (Test-Path (Join-Path $r 'Program Files\Shabpa')) { $e += 'program folder still there' }
  if (Test-Path (Join-Path $r 'Start\Shabpa.lnk')) { $e += 'start menu link still there' }
  $e
}

# 13 registry failure (deny writes on the uninstall key) -> exit 12
$r = NewRoot 'regdeny'
Check (Run-Case 'install before deny' $r '--quiet') 0
$kp = Get-ChildItem 'HKCU:\Software\ShabpaSetupTest' | Where-Object { (Get-ItemProperty (Join-Path $_.PSPath 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Shabpa') -ErrorAction SilentlyContinue).InstallLocation -like "$r*" } | Select-Object -First 1
$sub = 'Software\ShabpaSetupTest\' + $kp.PSChildName + '\Software\Microsoft\Windows\CurrentVersion\Uninstall\Shabpa'
$rights = [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree
$perm = [Security.AccessControl.RegistryRights]'ReadPermissions,ChangePermissions,SetValue,CreateSubKey'
$rule = New-Object Security.AccessControl.RegistryAccessRule([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'SetValue,CreateSubKey', 'Deny')
$k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub, $rights, $perm); $acl = $k.GetAccessControl(); $acl.AddAccessRule($rule); $k.SetAccessControl($acl); $k.Close()
Check (Run-Case 'registry write denied (12)' $r '--quiet') 12

# 14 installer without payload -> exit 13
if ($NoPayloadExe) { Check (Run-Case 'no payload (13)' (NewRoot 'nopay') '--quiet' -ExePath $NoPayloadExe) 13 }

# cleanup: throwaway HKCU test keys and temp folders
try {
  $root = [Microsoft.Win32.Registry]::CurrentUser
  $perm = [Security.AccessControl.RegistryRights]'ReadPermissions,ChangePermissions'
  $top = $root.OpenSubKey('Software\ShabpaSetupTest')
  if ($top) {
    foreach ($n in $top.GetSubKeyNames()) {   # lift the deny rule of case 13 first
      $k = $root.OpenSubKey("Software\ShabpaSetupTest\$n\Software\Microsoft\Windows\CurrentVersion\Uninstall\Shabpa", [Microsoft.Win32.RegistryKeyPermissionCheck]::ReadWriteSubTree, $perm)
      if ($k) { $acl = $k.GetAccessControl(); foreach ($r in @($acl.GetAccessRules($true, $false, [Security.Principal.NTAccount]))) { if ($r.AccessControlType -eq 'Deny') { [void]$acl.RemoveAccessRule($r) } }; $k.SetAccessControl($acl); $k.Close() }
    }
    $top.Close()
    $root.DeleteSubKeyTree('Software\ShabpaSetupTest', $false)
  }
} catch { Write-Host "cleanup registry: $_" }
try { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
Write-Host ''
Write-Host "failures: $script:fail"
Write-Host '--- log of the happy path ---'
Write-Host $happyLog
exit $script:fail


