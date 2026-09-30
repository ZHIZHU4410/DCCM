# 用法：
#   .\push.ps1 -Message "改动说明" -Paths ChronoBlade/
#   .\push.ps1 -Message "改动说明" -Paths DashOverhaul/,PUSH_GUIDE.md
#   .\push.ps1 -Message "改动说明" -Paths DashOverhaul/ -Port 7890
#
# 设计要点（都是踩过的坑）：
#   1. 用 `git commit -m msg -- <pathspec>` 而不是 add + commit：
#      本仓库可能同时有别的进程/会话在提交，共享的 index 会被抢走，
#      add+commit 会变成"空提交"或把别人的文件夹带进来。
#      pathspec 提交只认你给的路径，跟 index 状态无关。
#   2. 每一步都检查 $LASTEXITCODE —— 否则 git 失败会被静默吞掉。
#   3. 提交后用 `git diff --name-only <提交前HEAD> HEAD` 复核，
#      不靠 `git show HEAD`（那可能已经是别人刚提交的那笔）。
#   4. 推送被拒（远端有新提交）自动 fetch + rebase 后重推。
param(
    [Parameter(Mandatory = $true)][string]$Message,
    [Parameter(Mandatory = $true)][string[]]$Paths,
    [int]$Port = 7897,
    [string]$Remote = "origin",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$proxy  = "http://127.0.0.1:$Port"
$target = "$Remote/$Branch"

function Fail([string]$msg) {
    Write-Host "[x] $msg" -ForegroundColor Red
    exit 1
}

# 1) 代理端口必须在监听（github.com:443 在本机被 SNI 阻断，直连必失败）
$ok = Test-NetConnection 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue
if (-not $ok) { Fail "代理 127.0.0.1:$Port 没在监听 —— 开代理客户端，或改用实际端口（-Port）" }

# 2) 先确认这些路径真的有改动（避免误提交空内容）
$changed = git status --short -- $Paths
if (-not $changed) { Fail "这些路径没有任何改动，先确认路径对不对：$($Paths -join ', ')" }
Write-Host "[i] 待提交的路径：" -ForegroundColor Cyan
$changed

# 3) pathspec 提交：只提交这些路径，index 里别人的东西一律不碰
$before = (git rev-parse HEAD).Trim()
git add -- $Paths
if ($LASTEXITCODE -ne 0) { Fail "git add 失败" }

git commit -m $Message -- $Paths
if ($LASTEXITCODE -ne 0) { Fail "git commit 失败（上面的输出就是原因）" }

$files = git diff --name-only $before HEAD
if (-not $files) { Fail "提交看似成功但没有任何文件变化，已中止（不要盲目推送）" }
Write-Host "[i] 本次提交 ($before -> $((git rev-parse HEAD).Trim()[0..6] -join '')) 包含 $($files.Count) 个文件：" -ForegroundColor Cyan
$files

# 4) 提示这次会推上去哪些提交（防止连带推上积压的提交）
Write-Host "[i] 本次将推送的提交（$target..HEAD）：" -ForegroundColor Cyan
git log --oneline "$target..HEAD"

# 5) 推送；被拒就 fetch + rebase 后重推一次
git -c "http.proxy=$proxy" push $Remote $Branch
if ($LASTEXITCODE -ne 0) {
    Write-Host "[!] 推送被拒，可能是远端有新提交；尝试 fetch + rebase 后重推" -ForegroundColor Yellow
    git -c "http.proxy=$proxy" fetch $Remote $Branch
    if ($LASTEXITCODE -ne 0) { Fail "fetch 失败，检查代理/网络（对照 PUSH_GUIDE.md 第 8 节）" }

    git rebase $target
    if ($LASTEXITCODE -ne 0) { Fail "rebase 出现冲突，手动解决后自己推一次" }

    git -c "http.proxy=$proxy" push $Remote $Branch
    if ($LASTEXITCODE -ne 0) { Fail "重推仍失败（对照 PUSH_GUIDE.md 第 8 节）" }
}

# 6) 验证远端 SHA
git -c "http.proxy=$proxy" fetch $Remote $Branch | Out-Null
$remote = (git rev-parse $target).Trim()
$local  = (git rev-parse HEAD).Trim()
Write-Host "[√] 完成：$target = $remote" -ForegroundColor Green
if ($remote -ne $local) {
    Write-Host "[!] 注意：本地 HEAD = $local，与远端不一致（可能别的进程又提交了）" -ForegroundColor Yellow
}
