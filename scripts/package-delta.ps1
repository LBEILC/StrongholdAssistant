param([Parameter(Mandatory)][string]$Previous, [Parameter(Mandatory)][string]$Current, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

function Read-Index($archive) {
    $files = @{}
    $dirs = @{}
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\', '/').TrimEnd('/')
        $parent = $name
        if ($entry.FullName.EndsWith('/')) { $dirs[$name] = $true }
        else { $files.Add($name, $entry) }
        while ($parent.Contains('/')) {
            $parent = $parent.Substring(0, $parent.LastIndexOf('/'))
            $dirs[$parent] = $true
        }
    }
    return @{ files = $files; dirs = $dirs }
}
function Get-EntryHash($entry) {
    $stream = $entry.Open()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)) } finally { $stream.Dispose() }
}
$sha = [Security.Cryptography.SHA256]::Create()
$previousZip = [IO.Compression.ZipFile]::OpenRead($Previous)
try {
    $currentZip = [IO.Compression.ZipFile]::OpenRead($Current)
    try {
        $old = Read-Index $previousZip
        $new = Read-Index $currentZip
        $changes = [ordered]@{
            added = @($new.files.Keys | Where-Object { !$old.files.ContainsKey($_) } | Sort-Object)
            modified = @($new.files.Keys | Where-Object { $old.files.ContainsKey($_) -and
                ($old.files[$_].Length -ne $new.files[$_].Length -or (Get-EntryHash $old.files[$_]) -ne (Get-EntryHash $new.files[$_])) } | Sort-Object)
            deleted = @($old.files.Keys | Where-Object { !$new.files.ContainsKey($_) } | Sort-Object)
            added_dir = @($new.dirs.Keys | Where-Object { !$old.dirs.ContainsKey($_) } | Sort-Object)
            deleted_dir = @($old.dirs.Keys | Where-Object { !$new.dirs.ContainsKey($_) } | Sort-Object)
        }
        $file = [IO.File]::Create($Output)
        try {
            $delta = [IO.Compression.ZipArchive]::new($file, 'Create')
            try {
                foreach ($name in ($changes.added + $changes.modified)) {
                    $inputStream = $new.files[$name].Open()
                    try {
                        $target = $delta.CreateEntry($name).Open()
                        try { $inputStream.CopyTo($target) } finally { $target.Dispose() }
                    } finally { $inputStream.Dispose() }
                }
                $writer = [IO.StreamWriter]::new($delta.CreateEntry('changes.json').Open(), [Text.UTF8Encoding]::new($false))
                try { $writer.Write(($changes | ConvertTo-Json)) } finally { $writer.Dispose() }
            } finally { $delta.Dispose() }
        } finally { $file.Dispose() }
    } finally { $currentZip.Dispose() }
} finally { $previousZip.Dispose(); $sha.Dispose() }
