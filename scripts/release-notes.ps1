<#
.SYNOPSIS
    Reads CHANGELOG.md - the one place release notes are written - for the three things
    that consume it.

.DESCRIPTION
    The GitHub release body used to be the single line "SQLBI Whiteboard <version>.", and
    turning the pipeline's changelog generator on would have replaced it with a list of
    commit subjects. Neither says what a person gets by upgrading, so the notes are written
    by hand in CHANGELOG.md and this script hands them to whoever needs them:

      Verify    - fails when the version being released has no entry. Runs on the pull
                  request that bumps VersionPrefix, so the notes are written while the
                  change is fresh, and again when a release is published.
      Markdown  - writes one version's entry out for the Azure Pipelines release task.
      Html      - renders every entry into site/changelog.html during the Pages deployment.

    Only released versions are in the file. Pre-release Dev builds are published from every
    merge to main and would bury the releases that matter.

.PARAMETER Mode
    Verify, Markdown, or Html.

.PARAMETER Version
    The version to act on, with or without a leading "v". Required by Verify and Markdown,
    ignored by Html.

.PARAMETER Path
    The changelog to read.

.PARAMETER OutFile
    Where Markdown writes. Defaults to standard output.

.PARAMETER Page
    The page Html rewrites, between its releases markers.
#>
[CmdletBinding()]
param(
    [ValidateSet('Verify', 'Markdown', 'Html')]
    [string]$Mode = 'Verify',
    [string]$Version,
    [string]$Path = 'CHANGELOG.md',
    [string]$OutFile,
    [string]$Page = 'site/changelog.html'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# "## 1.2.2 - 2 September 2026". The version is what the machine reads; everything after it
# is the date, shown as written rather than reformatted.
$headingPattern = '^##\s+(\d+\.\d+\.\d+)\s*[-–—]?\s*(.*)$'

function Get-Entries([string]$file) {
    if (-not (Test-Path $file)) {
        throw "No changelog at '$file'."
    }

    $entries = [System.Collections.Generic.List[object]]::new()
    $current = $null
    $body = [System.Collections.Generic.List[string]]::new()

    foreach ($line in (Get-Content $file)) {
        if ($line -match $headingPattern) {
            if ($current) {
                $current.Body = ($body -join "`n").Trim()
                $entries.Add($current)
            }

            $current = [pscustomobject]@{
                Version = $Matches[1]
                Date    = $Matches[2].Trim()
                Body    = ''
            }
            $body.Clear()
            continue
        }

        # Any other second-level heading ends the entry rather than joining it. Without
        # this a trailing section - "Before 1.0.0" was the one that caught it - is read as
        # part of the last release and rendered inside it.
        if ($line -match '^##\s') {
            if ($current) {
                $current.Body = ($body -join "`n").Trim()
                $entries.Add($current)
                $current = $null
            }

            continue
        }

        if ($current) {
            [void]$body.Add($line)
        }
    }

    if ($current) {
        $current.Body = ($body -join "`n").Trim()
        $entries.Add($current)
    }

    return $entries
}

function Get-Entry($entries, [string]$wanted) {
    if (-not $wanted) {
        throw "-Version is required for $Mode."
    }

    $number = $wanted.Trim() -replace '^v', ''
    $match = $entries | Where-Object { $_.Version -eq $number } | Select-Object -First 1
    if (-not $match) {
        throw @"
$Path has no entry for $number.

Add a '## $number - <date>' section saying what a person gets by upgrading, with one '###'
heading per thing they would notice. A release that only fixes something still needs one:
say what broke. Pre-release Dev builds are not listed and need no entry.
"@
    }

    return $match
}

# Deliberately not a Markdown implementation: it renders the subset CHANGELOG.md is allowed
# to use, and anything outside that subset arrives as plain text rather than as broken
# markup. The changelog is ours, so the subset is a style rule rather than a limitation.
function ConvertTo-Html([string]$markdown) {
    function Escape([string]$s) {
        return $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    }

    function Inline([string]$s) {
        $s = Escape $s
        $s = [regex]::Replace($s, '`([^`]+)`', '<code>$1</code>')
        $s = [regex]::Replace($s, '\[([^\]]+)\]\((https?://[^)\s]+)\)', '<a href="$2">$1</a>')
        $s = [regex]::Replace($s, '\*\*([^*]+)\*\*', '<strong>$1</strong>')
        return $s
    }

    $html = [System.Collections.Generic.List[string]]::new()
    $paragraph = [System.Collections.Generic.List[string]]::new()
    $inList = $false

    function Close-Paragraph {
        if ($script:paragraph.Count) {
            $script:html.Add('<p>' + (Inline ($script:paragraph -join ' ')) + '</p>')
            $script:paragraph.Clear()
        }
    }

    $script:html = $html
    $script:paragraph = $paragraph

    foreach ($line in ($markdown -split "`n")) {
        $trimmed = $line.Trim()

        if (-not $trimmed) {
            Close-Paragraph
            if ($inList) { $html.Add('</ul>'); $inList = $false }
            continue
        }

        if ($trimmed -match '^###\s+(.*)$') {
            Close-Paragraph
            if ($inList) { $html.Add('</ul>'); $inList = $false }
            $html.Add('<h3>' + (Inline $Matches[1]) + '</h3>')
            continue
        }

        if ($trimmed -match '^[-*]\s+(.*)$') {
            Close-Paragraph
            if (-not $inList) { $html.Add('<ul>'); $inList = $true }
            $html.Add('<li>' + (Inline $Matches[1]) + '</li>')
            continue
        }

        [void]$paragraph.Add($trimmed)
    }

    Close-Paragraph
    if ($inList) { $html.Add('</ul>') }
    return ($html -join "`n")
}

$entries = Get-Entries $Path

switch ($Mode) {
    'Verify' {
        $entry = Get-Entry $entries $Version
        Write-Host "$Path has notes for $($entry.Version) ($($entry.Date))."
    }

    'Markdown' {
        $entry = Get-Entry $entries $Version
        if ($OutFile) {
            $directory = Split-Path -Parent $OutFile
            if ($directory -and -not (Test-Path $directory)) {
                New-Item -ItemType Directory -Path $directory -Force | Out-Null
            }

            Set-Content -Path $OutFile -Value $entry.Body -Encoding utf8NoBOM
            Write-Host "Wrote the $($entry.Version) notes to $OutFile."
        }
        else {
            $entry.Body
        }
    }

    'Html' {
        if (-not (Test-Path $Page)) {
            throw "No page at '$Page'."
        }

        $sections = foreach ($entry in $entries) {
            # The separator is a character rather than a CSS gap: without it, "1.2.2" and
            # "2 September 2026" run together into "1.2.22 September 2026" wherever the
            # stylesheet does not reach - a reader view, a feed, a plain-text scrape.
            $date = if ($entry.Date) {
                "<span class=""relsep"" aria-hidden=""true"">&middot;</span><span class=""when"">$($entry.Date)</span>"
            }
            else { '' }
            @"
<section class="release">
<h2><span class="ver">$($entry.Version)</span>$date</h2>
$(ConvertTo-Html $entry.Body)
</section>
"@
        }

        # Markers rather than a whole generated page: the surrounding chrome - nav, footer,
        # the explanation of the two channels - is hand-written and has nothing to do with
        # any release.
        $start = '<!-- releases:start -->'
        $end = '<!-- releases:end -->'
        $text = Get-Content $Page -Raw
        if ($text -notmatch [regex]::Escape($start) -or $text -notmatch [regex]::Escape($end)) {
            throw "'$Page' is missing the $start and $end markers this writes between."
        }

        $replacement = $start + "`n" + ($sections -join "`n") + "`n" + $end
        $pattern = [regex]::Escape($start) + '.*?' + [regex]::Escape($end)
        $updated = [regex]::Replace($text, $pattern, { $replacement }, 'Singleline')
        Set-Content -Path $Page -Value $updated -Encoding utf8NoBOM
        Write-Host "Wrote $($entries.Count) release$(if ($entries.Count -ne 1) { 's' }) into $Page."
    }
}
