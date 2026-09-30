# 用法：
#   .\push.ps1 -Message "改动说明" -Paths ChronoBlade/
#   .\push.ps1 -Message "改动说明" -Paths DashOverhaul/ -Port 7897
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [int]$Port = 7897,
    [string]$Remote = "origin",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$proxy = "http://127.0.0.1:$Port"

# 1) 代理端口必须在监听
$ok = Test-NetConnection 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
if (-not $ok) { Write-Host "[x] 代理 127.0.0.1:$Port 没在监听 —— 开代理客户端，或改用实际端口" -ForegroundColor Red; exit 1 }

# 2) 只 stage 指定路径
git add -- $Paths
$staged = git diff --cached --name-only
if (-not $staged) { Write-Host "[x] 没有暂存任何改动，先确认路径对不对" -ForegroundColor Red; exit 1 }
Write-Host "[i] 本次将提交：" -ForegroundColor Cyan; $staged

# 3) 提交
git commit -m $Message

# 4) 先给用户看将要推的提交（防止连带推上一堆积压提交）
Write-Host "[i] 本次将推送的提交：" -ForegroundColor Cyan
git log --oneline "$Remote/$Branch..HEAD"

# 5) 推 + 验证
git -c "http.proxy=$proxy" push $Remote $Branch
if ($LASTEXITCODE -ne 0) { Write-Host "[x] 推送失败，见上面的报错，对照 PUSH_GUIDE.md 第 8 节" -ForegroundColor Red; exit 1 }
git -c "http.proxy=$proxy" fetch $Remote $Branch | Out-Null
Write-Host ("[√] 完成：远端 {0}/{1} = {2}" -f $Remote, $Branch, (git rev-parse "$Remote/$Branch")) -ForegroundColor Green
