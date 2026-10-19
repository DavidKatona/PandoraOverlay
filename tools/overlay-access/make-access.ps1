# Writes the site's ACCESS HEADER, scrambled, into Assets/overlay-access.bin.
#
# Since Oct 2026 the website's anti-abuse rule lets the overlay through four of its
# endpoints (mylocation, friends, the Prime check and its cooldown) only with a header
# the website team gave the overlay. Its name and value are a credential, like the
# cookie (CLAUDE.md, hard constraint 4): they are NEVER written into this repository,
# not into code, docs, tests or a commit message. They live in the GitHub Actions
# secret PANDORA_OVERLAY_ACCESS, in the owner's own records, and in the file this
# script writes, which .gitignore keeps out of git and the build embeds
# (Core/OverlayAccess.cs reads it).
#
# The input is ONE line: the header's name, a colon, its value. The same rules as
# OverlayAccess.Unscramble: the name is letters, digits and hyphens; the value is
# visible ASCII without spaces. The file is N random pad bytes (fresh on every run),
# then the line's N bytes each XOR-ed with the pad byte at the same position, so no
# text search of the file or of the program finds the line. It is a speed bump, not a
# lock: whoever runs the overlay can read the header off their own traffic.
#
# The script NEVER prints the line or any part of it, error messages included, and
# makes no request. A re-run overwrites the file (with a new pad).
#
# Usage, on the owner's PC (Windows PowerShell 5.1), from the repo folder:
#   powershell -File tools\overlay-access\make-access.ps1
# then paste the line at the hidden prompt (what is typed is not shown and is not kept
# in PowerShell's history). Do NOT pass the line on the command line or set it in an
# environment variable by hand: either would land in the shell's history.
#
# In the release workflow (.github/workflows/release.yml) the line comes from the
# secret through the PANDORA_OVERLAY_ACCESS environment variable, and there an empty
# or missing secret is an error rather than a prompt.
param(
    [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
# Resolved here: $PSScriptRoot can be empty in a param block under "powershell -File".
if ($Out -eq '') { $Out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..\Assets\overlay-access.bin' }
function Fail([string]$why) { throw "make-access: $why" }

# --- the line -----------------------------------------------------------------------
$line = $env:PANDORA_OVERLAY_ACCESS
if ([string]::IsNullOrEmpty($line)) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Fail 'the PANDORA_OVERLAY_ACCESS secret is missing or empty' }
    $secure = Read-Host -AsSecureString 'Paste the access line (name:value)'
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { $line = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}
$line = $line.Trim()

# The checks never quote the line: a mistake is described, never shown.
$colon = $line.IndexOf(':')
if ($colon -lt 1) { Fail 'the line must be the header name, a colon, then the value (no name found)' }
if ($colon -eq $line.Length - 1) { Fail 'the line must be the header name, a colon, then the value (no value found)' }
if (-not [regex]::IsMatch($line.Substring(0, $colon), '^[A-Za-z0-9-]+$')) { Fail 'the name may hold only letters, digits and hyphens' }
if (-not [regex]::IsMatch($line.Substring($colon + 1), '^[\x21-\x7E]+$')) { Fail 'the value may hold only visible ASCII characters, without spaces' }

# --- scramble and write -------------------------------------------------------------
$plain = [Text.Encoding]::ASCII.GetBytes($line)
$line = $null
$n = $plain.Length
$pad = New-Object byte[] $n
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($pad) } finally { $rng.Dispose() }
$file = New-Object byte[] ($n * 2)
[Array]::Copy($pad, 0, $file, 0, $n)
for ($i = 0; $i -lt $n; $i++) { $file[$n + $i] = $plain[$i] -bxor $pad[$i] }

$dir = Split-Path -Parent $Out
if (-not (Test-Path $dir)) { Fail "no folder at $dir" }
[IO.File]::WriteAllBytes($Out, $file)

# Read it back and undo the scrambling, the way the overlay will.
$back = [IO.File]::ReadAllBytes($Out)
$same = $back.Length -eq $n * 2
for ($i = 0; $same -and $i -lt $n; $i++) { $same = ($back[$i] -bxor $back[$n + $i]) -eq $plain[$i] }
[Array]::Clear($plain, 0, $n)
if (-not $same) { Remove-Item $Out; Fail 'the written file did not read back correctly, and was removed' }

Write-Output ("make-access: wrote {0} ({1} bytes)" -f (Resolve-Path $Out), $back.Length)
