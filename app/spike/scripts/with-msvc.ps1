# Runs a command inside the VS 2019 Build Tools x64 environment. Without it, rustc picks the newest Visual Studio
# (18 Community here), which has no C++ libraries installed, and linking fails with LNK1104 msvcrt.lib.
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]] $Command)

$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
cmd /c "`"$vcvars`" >nul && set" | ForEach-Object {
    if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2] }
}
$env:Path = "$env:USERPROFILE\.cargo\bin;$env:Path"
& $Command[0] @($Command | Select-Object -Skip 1)
exit $LASTEXITCODE
