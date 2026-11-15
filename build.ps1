param([string]$PythonExe = 'python')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$build = Join-Path $root '.build'
$release = Join-Path $root 'dist'
$stage = Join-Path $build ('package-' + [Guid]::NewGuid().ToString('N'))
$runtime = Join-Path $stage 'runtime'
$library = Join-Path $runtime 'Lib'
$packages = Join-Path $library 'site-packages'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$icon = Join-Path $root 'assets\JinPDF.ico'
$base = & $PythonExe -c 'import sys, struct; assert struct.calcsize("P") == 8; print(sys.base_prefix)'
if ($LASTEXITCODE -ne 0) { throw '64-bit Windows Python is required.' }
$site = & $PythonExe -c 'from pathlib import Path; import PIL,pypdf,pypdfium2; print(Path(PIL.__file__).parent.parent)'
if ($LASTEXITCODE -ne 0) { throw 'Run pip install -r requirements.txt first.' }

New-Item -ItemType Directory -Force -Path $packages,$release | Out-Null
try {
    $sources = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
    & $compiler /nologo /target:winexe /platform:x64 /codepage:65001 /optimize+ /utf8output `
        "/out:$(Join-Path $stage 'JinPDF.exe')" "/win32icon:$icon" "/resource:$icon,JinPDF.ico" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
        /reference:System.Web.Extensions.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'The Windows app failed to compile.' }

    Get-ChildItem $base -File | Where-Object Name -Match '^(python\.exe|python[0-9]+\.dll|vcruntime.*\.dll|LICENSE\.txt)$' |
        Copy-Item -Destination $runtime
    Copy-Item (Join-Path $base 'DLLs') -Destination $runtime -Recurse
    $excluded = @('site-packages','test','tkinter','idlelib','turtledemo','ensurepip','__pycache__')
    & robocopy (Join-Path $base 'Lib') $library /E /XD $excluded /XF '*.pyc' /R:0 /W:0 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'The Python standard library could not be copied.' }
    $includes = @('pypdf','pypdfium2*','PIL','pillow.libs','pypdf-*.dist-info','pillow-*.dist-info')
    foreach ($pattern in $includes) {
        Get-ChildItem -Path $site -Filter $pattern | Copy-Item -Destination $packages -Recurse
    }
    $env:JINPDF_BUNDLE_RUNTIME = $runtime
    & (Join-Path $runtime 'python.exe') -I -B -c 'import os,sys; assert os.path.samefile(sys.base_prefix,os.environ["JINPDF_BUNDLE_RUNTIME"]); from PIL import Image; import pypdf, pypdfium2; Image.new("RGB", (10, 10)).tobytes()'
    if ($LASTEXITCODE -ne 0) { throw 'The bundled PDF engine failed its import check.' }
    Copy-Item (Join-Path $root 'src\engine.py'),(Join-Path $root 'src\office.ps1'),(Join-Path $root 'README.md') -Destination $stage
    Copy-Item (Join-Path $root 'samples') -Destination $stage -Recurse
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath (Join-Path $release 'JinPDF-v1.1.1-win-x64.zip') -Force
    Compress-Archive -Path (Join-Path $root 'samples\*') -DestinationPath (Join-Path $release 'JinPDF-v1.1.1-samples.zip') -Force
    Get-FileHash -Algorithm SHA256 (Join-Path $release 'JinPDF-v1.1.1-*.zip') | Format-Table -AutoSize
} finally {
    # The unique staging directory belongs to this build.
    $stageFull = [IO.Path]::GetFullPath($stage)
    $buildPrefix = [IO.Path]::GetFullPath($build) + [IO.Path]::DirectorySeparatorChar
    if ($stageFull.StartsWith($buildPrefix, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $stageFull)) {
        Remove-Item -LiteralPath $stageFull -Recurse -Force
    }
}
