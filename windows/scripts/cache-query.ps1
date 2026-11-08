# Sends one request to GitVeltoCache.exe and prints the answer (debugging aid).
#   powershell -File windows/scripts/cache-query.ps1 "dir`tC:\path\to\folder"
param([Parameter(Mandatory = $true)][string]$Request)
$name = "GitVeltoCache-$env:USERNAME"
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $name, [System.IO.Pipes.PipeDirection]::InOut)
$pipe.Connect(2000)
$bytes = [System.Text.Encoding]::UTF8.GetBytes($Request + "`n")
$pipe.Write($bytes, 0, $bytes.Length)
$pipe.Flush()
$reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
$reader.ReadToEnd()
$pipe.Dispose()
