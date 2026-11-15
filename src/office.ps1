param(
    [Parameter(Mandatory=$true)][string]$SourcePath,
    [Parameter(Mandatory=$true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$app = $null
$document = $null
$isExcel = [IO.Path]::GetExtension($SourcePath) -in '.xls', '.xlsx'
try {
    if ($isExcel) {
        $app = New-Object -ComObject Excel.Application
        $app.DisplayAlerts = $false
        $app.AutomationSecurity = 3
        $document = $app.Workbooks.Open($SourcePath, 0, $true)
        $document.ExportAsFixedFormat(0, $OutputPath, 0, $true, $false)
    } else {
        $app = New-Object -ComObject PowerPoint.Application
        $oldSecurity = $app.AutomationSecurity
        $app.AutomationSecurity = 3
        try {
            $document = $app.Presentations.Open($SourcePath, -1, 0, 0)
        } finally {
            $app.AutomationSecurity = $oldSecurity
        }
        $document.SaveAs($OutputPath, 32)
    }
} catch {
    [Console]::WriteLine($_.Exception.Message)
    exit 1
} finally {
    if ($null -ne $document) {
        if ($isExcel) { $document.Close($false) } else { $document.Close() }
    }
    if ($null -ne $app -and $isExcel) { $app.Quit() }
}
