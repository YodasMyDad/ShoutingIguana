# What the shell's Open folder and Open file opened, seen from outside, and closing just those again (the smoke walk's
# export step). Never closes anything else: an Explorer window is matched by its folder, an Excel workbook by its full
# path, and Excel itself is quit only when that workbook was all it had open.
#   shell-opened.ps1 folder <folder>   -> "yes" when an Explorer window shows the folder (and closes it), else "no"
#   shell-opened.ps1 file <file>       -> the program that has the file open ("Excel", or a window's title), else "no"
#   shell-opened.ps1 close-file <file> <since> -> closes the file where it was opened. <since> is when the walk asked
#     for it (Unix ms): an Excel that wasn't running then is the walk's, and is closed with the file.
param([Parameter(Mandatory)] [string] $What, [Parameter(Mandatory)] [string] $Target, [long] $Since = 0)
$ErrorActionPreference = 'Stop'

function Same([string] $a, [string] $b) {
  return [IO.Path]::GetFullPath($a).TrimEnd('\') -ieq [IO.Path]::GetFullPath($b).TrimEnd('\')
}

switch ($What) {
  'folder' {
    $shell = New-Object -ComObject Shell.Application
    foreach ($window in @($shell.Windows())) {
      try { $at = $window.Document.Folder.Self.Path } catch { continue }
      if ($at -and (Same $at $Target)) {
        $window.Quit()
        'yes'
        return
      }
    }
    'no'
  }
  'file' {
    try {
      $excel = [Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
      foreach ($book in @($excel.Workbooks)) {
        if (Same $book.FullName $Target) { 'Excel'; return }
      }
    } catch { }
    $name = [IO.Path]::GetFileName($Target)
    $window = Get-Process | Where-Object { $_.MainWindowTitle -like "*$name*" } | Select-Object -First 1
    if ($window) { $window.MainWindowTitle } else { 'no' }
  }
  'close-file' {
    # Through Excel's own object where it can be reached: the workbook alone, and Excel only if it was all it had.
    $closed = $false
    for ($try = 0; $try -lt 5 -and -not $closed; $try++) {
      try {
        $excel = [Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application')
        foreach ($book in @($excel.Workbooks)) {
          if (Same $book.FullName $Target) { $book.Close($false); $closed = $true }
        }
        if ($closed -and $excel.Workbooks.Count -eq 0) { $excel.Quit() }
      } catch { Start-Sleep -Milliseconds 500 }
    }
    if ($closed) { 'closed'; return }
    # An Excel that has just started isn't reachable that way until it has lost focus. One that started after the walk
    # asked, showing this file, is the walk's own: its window is closed, then the process if it stays without one.
    $name = [IO.Path]::GetFileName($Target)
    $after = [DateTimeOffset]::FromUnixTimeMilliseconds($Since).LocalDateTime
    $ours = Get-Process EXCEL -ErrorAction SilentlyContinue |
      Where-Object { $_.StartTime -ge $after -and $_.MainWindowTitle -like "$name*" }
    foreach ($process in @($ours)) {
      $process.CloseMainWindow() | Out-Null
      if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id -Force }
    }
    if (@($ours).Count -gt 0) { 'closed' } else { 'not found' }
  }
  default { throw "Unknown: $What" }
}
