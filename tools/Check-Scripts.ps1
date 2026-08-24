<#
.SYNOPSIS
  A fast structural sanity check over Assets/Scripts. Not a compiler — a tripwire.

.DESCRIPTION
  Unity is the real check and always will be. This exists for the case Unity cannot cover: work done
  on a machine with no editor and no .NET SDK, where the first sign that a file is broken is the next
  person opening the project. It caught nothing for months and then earned itself in one go, when a
  stray fragment of an expression ended up sitting above the using directives in PlanetViewWindow.cs
  and reached main.

  Nine checks, chosen because each catches something the others cannot:

    HEAD     The first non-blank line of a file must look like the top of a C# file. This is the one
             that catches text pasted or dropped in above the usings.
    BALANCE  Braces and parens must balance, and must never dip below zero on the way. Catches a
             truncated or duplicated block anywhere in a file.
    ENUMS    Every `SomeEnum.Member` reference must name a member that exists. Balance would never
             have caught the fragment that started this, because the identifier in it was TRUNCATED —
             `SurfaceIndexKind.Minera` — and a truncated identifier is perfectly balanced.
    STRING   Every regular string literal must close on the line it opens. Delegated to
             tools/check-string-literals.mjs — see the note beside the call for why it is here.
    STATIC   Every `SomeType.Member` reference onto a project type must name a real member, which is
             the ENUMS check applied to classes. Delegated to tools/check-static-refs.mjs.
    MEMBER   The same question for a LOCAL whose type can be resolved. Delegated to
             tools/check-member-refs.mjs.
    FOREACH  Whatever a foreach iterates has to BE iterable — CS1579. Delegated to
             tools/check-foreach.mjs.
    UNITY    No `??` or `?.` on a UnityEngine.Object. Those are C# operators and never run Unity's
             == overload, so a DESTROYED component sails through them as though it were alive and
             the next call throws MissingComponentException. The one check here that catches a
             runtime fault rather than a compile error, and it is here because that fault reads
             exactly like a compile error in the console. Delegated to tools/check-unity-null.mjs.
    SCOPE    No local may shadow one from an ENCLOSING scope — CS0136. Every other check here asks
             about a NAME: does this member exist, does this string close. This one asks where a name
             is LIVE, which is the thing a regex over `Type.Member` cannot see, and it is here because
             one reached main on 2026-08-23 past all eight of the others. Delegated to
             tools/check-scope.mjs.

  The six delegated checks need `node` on PATH. If it is missing they are reported as NOT RUN and
  the script exits 1 — "Clean." has to mean all nine ran.

  Comments, strings, verbatim strings and char literals are stripped before counting, so a brace in a
  comment or a paren in a message cannot produce a false alarm.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/Check-Scripts.ps1

.OUTPUTS
  Exit code 0 if clean, 1 if anything is suspect. Findings go to stdout, one per line.
#>
[CmdletBinding()]
param(
    [string] $Root
)

# Resolve the default here rather than in the param block: $PSScriptRoot is not reliably populated
# while parameter defaults are being evaluated, and a check that cannot find the code it checks is
# worse than no check.
if ([string]::IsNullOrWhiteSpace($Root)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Root = Join-Path (Split-Path -Parent $here) 'Assets/Scripts'
}

if (-not (Test-Path $Root)) { Write-Error "No such directory: $Root"; exit 1 }
$Root = (Resolve-Path $Root).Path

# Strip comments, strings and char literals so only code is counted. Interpolated strings keep the
# braces of their holes, so an unbalanced brace inside one is still caught.
function Get-CodeOnly {
    param([string] $Src)
    $sb = New-Object System.Text.StringBuilder
    $i = 0; $n = $Src.Length; $bs = [char]92; $q = [char]34; $sq = [char]39
    while ($i -lt $n) {
        $c = $Src[$i]
        $d = ''; if ($i + 1 -lt $n) { $d = $Src[$i + 1] }
        if ($c -eq '/' -and $d -eq '/') { while ($i -lt $n -and $Src[$i] -ne "`n") { $i++ }; continue }
        if ($c -eq '/' -and $d -eq '*') {
            $i += 2
            # Newlines are KEPT while the comment is discarded, so reported line numbers stay true. A
            # finding that names the wrong line is a finding the reader has to go hunting for.
            while ($i + 1 -lt $n -and -not ($Src[$i] -eq '*' -and $Src[$i + 1] -eq '/')) {
                if ($Src[$i] -eq "`n") { [void]$sb.Append("`n") }
                $i++
            }
            $i += 2; continue
        }
        if ($c -eq '$' -and $d -eq $q) {
            $i += 2; $depth = 0
            while ($i -lt $n) {
                if ($Src[$i] -eq $bs) { $i += 2; continue }
                if ($Src[$i] -eq '{') { $depth++; [void]$sb.Append('{'); $i++; continue }
                if ($Src[$i] -eq '}') { if ($depth -gt 0) { $depth--; [void]$sb.Append('}') }; $i++; continue }
                if (($Src[$i] -eq '(' -or $Src[$i] -eq ')') -and $depth -gt 0) { [void]$sb.Append($Src[$i]); $i++; continue }
                if ($Src[$i] -eq $q -and $depth -eq 0) { $i++; break }
                $i++
            }
            continue
        }
        if ($c -eq '@' -and $d -eq $q) {
            $i += 2
            while ($i -lt $n) {
                if ($Src[$i] -eq $q -and $i + 1 -lt $n -and $Src[$i + 1] -eq $q) { $i += 2; continue }
                if ($Src[$i] -eq $q) { $i++; break }
                if ($Src[$i] -eq "`n") { [void]$sb.Append("`n") }   # verbatim strings span lines too
                $i++
            }
            continue
        }
        if ($c -eq $q) {
            $i++
            while ($i -lt $n) {
                if ($Src[$i] -eq $bs) { $i += 2; continue }
                if ($Src[$i] -eq $q) { $i++; break }
                $i++
            }
            continue
        }
        if ($c -eq $sq) {
            $i++
            while ($i -lt $n) {
                if ($Src[$i] -eq $bs) { $i += 2; continue }
                if ($Src[$i] -eq $sq) { $i++; break }
                $i++
            }
            continue
        }
        [void]$sb.Append($c); $i++
    }
    return $sb.ToString()
}

$files = Get-ChildItem -Path $Root -Recurse -Filter *.cs -File
$sources = @{}
foreach ($f in $files) { $sources[$f.FullName] = [System.IO.File]::ReadAllText($f.FullName).TrimStart([char]0xFEFF) }

# ---- Collect every enum and its members -------------------------------------------------------
$enums = @{}
$enumRe = [regex] 'enum\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*[A-Za-z]+\s*)?\{([^}]*)\}'
foreach ($text in $sources.Values) {
    foreach ($m in $enumRe.Matches($text)) {
        $name = $m.Groups[1].Value
        # Comments FIRST: a comma inside a // comment would otherwise split the member list.
        $body = [regex]::Replace($m.Groups[2].Value, '/\*[\s\S]*?\*/', ' ')
        $body = [regex]::Replace($body, '//[^\r\n]*', ' ')
        if (-not $enums.ContainsKey($name)) { $enums[$name] = New-Object 'System.Collections.Generic.HashSet[string]' }
        foreach ($piece in $body.Split(',')) {
            $v = $piece.Split('=')[0].Trim()
            if ($v -match '^[A-Za-z_][A-Za-z0-9_]*$') { [void]$enums[$name].Add($v) }
        }
    }
}

$findings = New-Object System.Collections.ArrayList
$headRe = [regex] '^(using |//|/\*|namespace |\[|public |internal |static |partial |abstract |sealed |#|enum |class |struct )'
$refRe  = [regex] '(?<![.A-Za-z0-9_])([A-Z][A-Za-z0-9_]*)\.([A-Za-z_][A-Za-z0-9_]*)'

foreach ($path in $sources.Keys) {
    $text  = $sources[$path]
    $short = $path.Substring($Root.Length).TrimStart([char]92, [char]47)

    # ---- HEAD ----
    $first = ($text -split "`r?`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 1)
    if ($null -ne $first -and -not $headRe.IsMatch($first.Trim())) {
        [void]$findings.Add("HEAD     $short  ->  $($first.Trim())")
    }

    $code = Get-CodeOnly $text

    # ---- BALANCE ----
    $depth = 0; $minDepth = 0; $par = 0; $minPar = 0
    foreach ($ch in $code.ToCharArray()) {
        switch ($ch) {
            '{' { $depth++ }
            '}' { $depth--; if ($depth -lt $minDepth) { $minDepth = $depth } }
            '(' { $par++ }
            ')' { $par--; if ($par -lt $minPar) { $minPar = $par } }
        }
    }
    if ($depth -ne 0 -or $minDepth -lt 0) { [void]$findings.Add("BRACE    $short  (ends at $depth, dips to $minDepth)") }
    if ($par -ne 0 -or $minPar -lt 0)     { [void]$findings.Add("PAREN    $short  (ends at $par, dips to $minPar)") }

    # ---- ENUMS ----
    $lines = $code -split "`r?`n"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        foreach ($m in $refRe.Matches($lines[$i])) {
            $type = $m.Groups[1].Value; $member = $m.Groups[2].Value
            if (-not $enums.ContainsKey($type)) { continue }
            if ($enums[$type].Contains($member)) { continue }
            $after = $lines[$i].Substring($m.Index + $m.Length)
            if ($after -match '^\s*\(') { continue }   # a method call on a same-named class
            [void]$findings.Add("ENUM     $short`:$($i + 1)  ->  $type.$member is not a member")
        }
    }
}

# ---- DELEGATED CHECKS ---------------------------------------------------------------------------
#
# Two things the three checks above cannot see, both of which have now cost real time:
#
#   STRING  An unterminated string literal. A stray newline inside one leaves the head line, the
#           braces, the parens and every enum reference perfectly intact -- and it is CS1010 followed
#           by a cascade of spurious errors pointing everywhere except at the fault. It reached Unity
#           exactly once, as eighteen errors in one file, and this script said "Clean." the whole way.
#   STATIC  A `SomeType.Member` reference to a member that no longer exists. Rename or delete a static
#           method and every call site still looks perfectly well-formed.
#
# Both rules live in their own .mjs rather than being ported into PowerShell here: two implementations
# of one rule drift apart, and the one that drifts is the one nobody is running. So this delegates --
# and SAYS SO when it cannot, because a check that silently skips is worse than no check. The "Clean."
# line goes on claiming the same thing either way.
#
# NOTE: ASCII only inside the code strings below. This file is UTF-8 with no BOM and Windows
# PowerShell 5.1 decodes it as ANSI, so a non-ASCII character in a STRING becomes mojibake the parser
# chokes on. In comments it is harmless, which is why the em dashes further up are left alone.
$toolsDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($toolsDir)) { $toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
$node = Get-Command node -ErrorAction SilentlyContinue

$delegated = @(
    @{ Tag = 'STRING'; Script = 'check-string-literals.mjs'; Noun = 'unterminated string literal(s)' },
    @{ Tag = 'STATIC'; Script = 'check-static-refs.mjs';     Noun = 'suspect static reference(s)' },
    @{ Tag = 'MEMBER'; Script = 'check-member-refs.mjs';     Noun = 'suspect member access(es)' },
    @{ Tag = 'FOREACH'; Script = 'check-foreach.mjs';        Noun = 'un-iterable foreach source(s)' },
    @{ Tag = 'UNITY'; Script = 'check-unity-null.mjs';       Noun = 'Unity fake-null operator(s)' },
    @{ Tag = 'SCOPE'; Script = 'check-scope.mjs';            Noun = 'shadowed local(s)' }
)

$skipped = New-Object System.Collections.ArrayList
$details = New-Object System.Collections.ArrayList
foreach ($check in $delegated) {
    $script = Join-Path $toolsDir $check.Script
    if (-not (Test-Path $script)) {
        [void]$skipped.Add("$($check.Tag): $($check.Script) is missing from $toolsDir")
        continue
    }
    if ($null -eq $node) {
        [void]$skipped.Add("$($check.Tag): node is not on PATH")
        continue
    }
    $out = & $node.Source $script
    if ($LASTEXITCODE -ne 0) {
        $bad = @($out | Select-String -Pattern '^FAIL ').Count
        [void]$findings.Add("$($check.Tag)   $bad $($check.Noun) - listed below")
        [void]$details.Add($out)
    }
}

Write-Output "Checked $($files.Count) C# files and $($enums.Count) enums."

# A skip is reported as a FAILURE rather than a pass with a footnote. This script's whole job is to
# stand in for a compiler that is not here, so "Clean." has to mean all nine checks ran and all nine
# passed — anything less is a claim it cannot back.
if ($skipped.Count -gt 0) {
    Write-Output ""
    foreach ($s in $skipped) { Write-Output "DID NOT RUN  $s" }
    Write-Output ""
    Write-Output "Head, balance and enums are clean; the checks above did NOT run, so what they cover"
    Write-Output "is UNCHECKED. Install node, or run those scripts yourself, before treating this as a"
    Write-Output "green light."
    exit 1
}

if ($findings.Count -eq 0) {
    Write-Output "Clean."
    exit 0
}
Write-Output ""
foreach ($f in $findings) { Write-Output $f }
foreach ($block in $details) {
    Write-Output ""
    foreach ($line in $block) { Write-Output $line }
}
Write-Output ""
Write-Output "$($findings.Count) finding(s)."
exit 1
