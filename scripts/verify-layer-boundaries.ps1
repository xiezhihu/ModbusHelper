$错误数 = 0
$项目根目录 = Split-Path -Parent $PSScriptRoot
$待检查目录 = @(
    (Join-Path $项目根目录 'src/ModbusHelper.Core'),
    (Join-Path $项目根目录 'src/ModbusHelper.Communication'),
    (Join-Path $项目根目录 'src/ModbusHelper.Business')
)

$禁止模式 = @(
    'using\s+System\.Windows\.Forms',
    'using\s+System\.Drawing',
    'System\.Windows\.Forms\.',
    'MessageBox',
    'BeginInvoke',
    '\bControl\b',
    '\bForm\b',
    '\bInvoke\b'
)

foreach ($目录 in $待检查目录) {
    if (-not (Test-Path -LiteralPath $目录)) {
        continue
    }

    $文件列表 = Get-ChildItem -LiteralPath $目录 -Recurse -Filter '*.cs' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    foreach ($文件 in $文件列表) {
        $内容 = Get-Content -LiteralPath $文件.FullName -Raw
        foreach ($模式 in $禁止模式) {
            if ($内容 -match $模式) {
                Write-Error "发现界面依赖：$($文件.FullName) 命中模式 $模式"
                $错误数++
            }
        }
    }
}

if ($错误数 -gt 0) {
    Write-Host "分层检查失败，共发现 $错误数 处界面依赖。" -ForegroundColor Red
    exit 1
}

Write-Host "分层检查通过：Core、Communication、Business 未发现界面依赖。" -ForegroundColor Green
