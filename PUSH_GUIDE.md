# 推送指南（DCCM 仓库 → GitHub）

> 仓库路径：`D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS`
> 远端仓库：<https://github.com/ZHIZHU4410/DCCM>
> 本文带 ✅ 的都是**实测结果**，最后验证：**2026-09-30**（同一台机器 `C:\Users\10113`）。
> 文档里凡是有可能会变的东西（代理端口、待推送提交数），都写了**怎么现场确认**，别记死。

---

## 0. 最快路径（TL;DR）

代理客户端开着的时候，一条命令搞定（用的是仓库自带的 [`push.ps1`](./push.ps1)，见附 B）：

```powershell
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"
.\push.ps1 -Message "本次改动说明" -Paths <你这次改的目录>
```

不想用脚本就手打这三条：

```powershell
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"

git add <你这次改的目录>          # ⚠️ 千万别 git add -A，见第 7 节
git commit -m "本次改动说明"
git -c http.proxy=http://127.0.0.1:7897 push origin main
```

两个必须记住的点：

- **必须写 `push origin main`，不能只写 `git push`** —— 当前分支跟踪的是 `upstream`(SSH)，而本机没有 SSH 私钥，裸 `git push` 会直接 `Permission denied (publickey)`。
- **必须带 `-c http.proxy=...`** —— `github.com:443` 在本机被按域名(SNI)阻断，直连必失败；git 里也没配任何代理。

---

## 1. 环境实测快照（照着核对）

| 项目 | 实测值 | 备注 |
|---|---|---|
| 当前分支 | `main` | |
| `origin` | `https://github.com/ZHIZHU4410/DCCM.git` | ✅ **推送用这个** |
| `upstream` | `git@github.com:ZHIZHU4410/DCCM.git` | 同一个仓库，只是协议不同，⚠️ 且本地记录很旧 |
| `branch.main.remote` | **`upstream`** | ⚠️ 这就是裸 `git push` 会走 SSH 的原因 |
| 提交身份 | `BLF4410 <1815130890@qq.com>` | `git config --global` |
| git 版本 | `2.53.0.windows.2` | |
| ssh 版本 | `OpenSSH_for_Windows_9.5p2, LibreSSL 3.8.2` | |
| PowerShell | `5.1.26100.8115`（**Windows PowerShell，不是 pwsh 7**） | ⚠️ 存 `.ps1` 必须 **UTF-8 with BOM**，见附 B |
| `~/.ssh` 内容 | **只有 `known_hosts`** | ⚠️ 没有私钥 → SSH 推送必失败 |
| `~/.ssh/config` | **不存在** | 走 SSH 才需要（见第 4 节） |
| 凭据助手 | `credential.helper=manager` | 来自**系统级** `D:/Git/etc/gitconfig`，不是 global |
| 已存凭据 | Windows 凭据管理器里有 `git:https://github.com`（用户 `ZHIZHU4410`） | 所以正常推送**不会**要求登录 |
| git 代理配置 | **全局 / 系统 / 本仓库全都没有** | 所以只能每次用 `-c` 显式指定 |
| DNS | `github.com` → `20.205.243.166`（真实 IP，未被污染） | |

### 网络端口实测

| 目标 | 结果 | 说明 |
|---|---|---|
| `github.com:443` | ❌ **FAIL**（`Recv failure: Connection was reset`） | SNI 阻断，HTTPS 直连没戏 |
| `github.com:22` | ✅ OK | SSH 默认端口反而通 |
| `ssh.github.com:443` | ✅ OK | SSH 的 443 备用入口 |
| `gitee.com:443` | ✅ OK | 说明不是整机断网 |
| `127.0.0.1:7897` | ✅ **OPEN** | 本机代理（Clash Verge 混合端口） |

### 两个远端的关系

两个远端指向**同一个 GitHub 仓库**，只是协议不同：

```
origin    https://github.com/...   ← 走 443，需要代理（被 SNI 阻断）
upstream  git@github.com:...       ← 走 22 / 443，不需要代理，但需要密钥
```

`upstream/main` 本地记录停在 `a084a83`，是**很早以前的一次 fetch 残留**（已确认是 `origin/main` 的祖先，不影响任何事）。
👉 **一律以 `origin/main` 为准。**

---

## 2. 结论：只有两条路能推上去

1. **开代理 + HTTPS**（第 3 节）—— 日常最快，推荐。
2. **配 SSH 密钥走 SSH**（第 4 节）—— 不想每次开代理就选这个，`github.com:22` 和 `ssh.github.com:443` 都是通的。

没有第三条「直连就能通」的路。

### ⚠️ 代理端口会变，别写死

| 时间 | 实际监听端口 |
|---|---|
| 之前（老配置里写死过） | `7890` |
| 现在 ✅ | **`7897`** |

「明明开了代理还是推不上去」，绝大多数情况就是**端口变了**，报错却长得像网络坏了。
每次推之前先确认一次（第 3 节第 2 步）。

---

## 3. 路线 A：代理 + HTTPS（推荐日常用）

### 步骤

**1) 打开代理客户端**，确认它监听哪个端口（Clash 类客户端界面里找「混合端口」）。

**2) 确认端口真的在监听**：

```powershell
Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet
# True  = 在监听
# False = 客户端没开，或端口不对（换成实际端口再试）
```

**3) 推送**（端口换成你实际的）：

```powershell
git -c http.proxy=http://127.0.0.1:7897 push origin main
```

### 想省掉每次输 `-c`？

写进**本仓库**配置即可（不动全局）：

```powershell
# 设置（只影响这个仓库）
git config http.proxy  http://127.0.0.1:7897
git config https.proxy http://127.0.0.1:7897
# 以后直接： git push origin main
```

```powershell
# 代理关了以后一定要取消，否则连 git status 都可能变慢
git config --unset http.proxy
git config --unset https.proxy
```

> ⚠️ **不建议写进 `--global`**：端口会变，写死之后下次客户端换端口，
> 你会拿到 `Failed to connect to 127.0.0.1 port 7890`，看起来像网络坏了，其实只是端口不对。
>
> ⚠️ 代理客户端开 **TUN / 系统代理** 模式时，git 可能不配代理也能直连。
> 判断方法：先试第 0 节那条带 `-c` 的命令，能过就说明通道是通的。

---

## 4. 路线 B：SSH（`github.com:22` 或 `ssh.github.com:443`）

适合「不想每次开代理」。两个入口 ✅ 实测都可达。

### 步骤

**1) 生成密钥**（本机目前没有私钥，这步必须）：

```powershell
ssh-keygen -t ed25519 -C "1815130890@qq.com"
# 一路回车即可（想设密码也行）
```

**2) 把公钥加到 GitHub**：打开 `C:\Users\10113\.ssh\id_ed25519.pub`，复制**全部内容**，
到 GitHub → Settings → SSH and GPG keys → New SSH key 粘贴保存。

**3) 写 SSH 配置**（文件：`C:\Users\10113\.ssh\config` —— **目前不存在，需要新建**）：

```
Host github.com
  HostName ssh.github.com
  Port 443
  User git
  IdentityFile ~/.ssh/id_ed25519
```

> 嫌 443 麻烦也可以不写 config，直接走默认 22（本机 22 是通的）：
> `git remote set-url upstream git@github.com:ZHIZHU4410/DCCM.git` 就行。

**4) 测试认证**：

```powershell
ssh -T git@github.com
# 成功： Hi ZHIZHU4410! You've successfully authenticated...
# 失败： git@github.com: Permission denied (publickey)
```

**5) 推送**：此时分支跟踪的就是 `upstream`(SSH)，裸 `git push` 也能用了；明确指定就是：

```powershell
git push upstream main
```

### 顺带把跟踪关系改回 origin（可选）

```powershell
git branch -u origin/main main
# 或者干脆统一协议： git remote set-url upstream https://github.com/ZHIZHU4410/DCCM.git
```

---

## 5. 路线 C：什么都不通时——打包带走

在能联网的机器上再推：

```powershell
git bundle create dccm.bundle origin/main..main
```

把 `.bundle` 拷到另一台机器，然后：

```powershell
git clone dccm.bundle myrepo
cd myrepo
git remote set-url origin https://github.com/ZHIZHU4410/DCCM.git
git push origin main
```

---

## 6. 凭据 / 登录（平时不用管，失效了才看）

**现状**：凭据由 **Git Credential Manager** 管理（系统级 `credential.helper=manager`），
并且 Windows 凭据管理器里**已经存了** `git:https://github.com`（用户 `ZHIZHU4410`）。
所以正常推送是**静默成功**的，不会弹登录框。

**什么时候会出问题**：Token 过期 / 被撤销 / 换了 GitHub 账号，报错通常是
`fatal: Authentication failed for 'https://github.com/ZHIZHU4410/DCCM.git/'`。

**处理办法**（任选）：

```powershell
# 1) 直接重推一次，GCM 会自动弹浏览器让你登录（推荐）
git -c http.proxy=http://127.0.0.1:7897 push origin main
```

```powershell
# 2) 先清掉旧凭据，再重推触发登录
cmdkey /list                       # 找到 target 为 git:https://github.com 的那条，确认用户
cmdkey /delete:git:https://github.com
# 如果提示找不到目标：用「控制面板 → 用户帐户 → 凭据管理器 → Windows 凭据」
# 手动删掉 git:https://github.com
```

> 本机 **没有** `git-credential-manager` 这个命令行（不在 PATH 里），
> 所以排查靠 `cmdkey` 或凭据管理器 GUI，别去敲 `git credential-manager ...`。
>
> ⚠️ 不要在聊天/文档里粘贴 GitHub Token、密码、恢复码。凭据只放在凭据管理器里。

---

## 7. 推送前的检查清单（这个仓库有坑）

### ⚠️ 最大的坑：仓库里全是**别的 mod** 的未提交改动

这是一个 DCCM 合集仓库，`git status` 会看到大量**与本次工作无关**的东西
（实测：**6 个已修改文件 + 65 项未跟踪目录**，属于 FlyingSword / Weaponbow /
BarrelLauncherOverhaul / ByCar 等其它 mod）。

**所以：绝对不要用 `git add -A` / `git add .` / `git commit -a`。**
只 stage 你这次真正改的路径：

```powershell
git add ChronoBlade/                     # 只加自己那个 mod 的目录
git status --short -- ChronoBlade/       # 复核一遍
git diff --cached --stat                 # 看清楚"到底会提交什么"
git commit -m "本次改动说明"
```

### ⚠️ 第二个坑：`git push origin main` 会把**所有**未推送的提交一起传上去

推送是 fast-forward，本地 `main` 领先 `origin/main` 多少就传多少 ——
**不是只传你刚提交的那一个**。

```powershell
git rev-list --count origin/main..HEAD   # 这次会传上去几个提交
git log --oneline origin/main..HEAD      # 具体是哪些（推之前务必看一眼）
```

> 2026-09-30 那次就是这样：本地积压了 21 个 ChronoBlade 提交，
> 加上新的 DashOverhaul 提交，**一次推上去 22 个**。想只推自己那个，
> 就得先 `git rebase`/另开分支，否则没法只推一个。

### 其它检查

```powershell
git rev-list --count HEAD..origin/main   # 必须为 0；>0 说明远端有新提交，要先 rebase
```

### 大文件（GitHub 单文件上限 100 MB，>50 MB 会警告）

| 文件 | 大小 |
|---|---|
| `ChronoBlade/ChronoBlade/Assets/atlas/TIMEBEIJING.png` | 9.95 MB |
| `ChronoBlade/ChronoBlade/Assets/atlas/TIMEKASAN.png` | 3.99 MB |
| `ChronoBlade/ChronoBlade/Assets/sfx/kurumi01~08.WAV` | 合计约 6.5 MB |
| `ChronoBlade/ChronoBlade/Assets/cardIcons.png` | 1.39 MB |

都在限内 ✅。

---

## 8. 报错对照表（都是本项目真实遇到过的）

| 报错 | 真正原因 | 解决 |
|---|---|---|
| `Recv failure: Connection was reset` | HTTPS 直连被 SNI 阻断（或没走代理） | 加 `-c http.proxy=http://127.0.0.1:<端口>` |
| `Failed to connect to github.com port 443 after 21097 ms` | TCP 都没通（代理没起 / 端口错） | 按第 3 节先确认代理和端口 |
| `Failed to connect to 127.0.0.1 port 7890` | **代理端口变了**（老配置写死 7890，实际 7897） | 查实际端口，改 `-c` 里的端口 |
| `Permission denied (publickey)` | 裸 `git push` 走了 `upstream`(SSH)，本机没私钥 | 用 `git push origin main`，或按第 4 节配 SSH |
| `fatal: Authentication failed` | 凭据过期/被撤销 | 见第 6 节 |
| `! [rejected] main -> main (non-fast-forward)` | 远端有你本地没有的提交 | `git fetch origin` → `git rebase origin/main` → 再推 |
| `MSB3021`（构建时） | 编译产物 dll 被占用（**游戏开着**） | 关掉游戏再 `dotnet build`；与推送无关 |
| `LF will be replaced by CRLF` | 换行符提示 | **不是错误**，忽略即可 |

---

## 9. 推送后怎么确认成功

```powershell
# 1) 远端记录的 main 应该等于你本地 HEAD
git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main
git rev-parse HEAD

# 2) 待推送数应该变成 0
git rev-list --count origin/main..HEAD

# 3) 刷新本地对远端的记录（可选）
git -c http.proxy=http://127.0.0.1:7897 fetch origin main

# 4) 直接看网页
start https://github.com/ZHIZHU4410/DCCM/commits/main
```

> `git ls-remote` 是**只读**的 —— 想验证「现在到底能不能推」，用它最合适，不会动远端。

---

## 10. 诊断命令速查（出问题按顺序跑）

| 命令 | 看什么 | 结论 |
|---|---|---|
| `git remote -v` | 两个远端的 URL | 确认推的目标是哪个 |
| `git config --get branch.main.remote` | `upstream` | **说明裸 `git push` 会走 SSH** |
| `Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet` | `True` | 代理在监听，端口记下来 |
| `Test-NetConnection github.com -Port 443 -InformationLevel Quiet` | `False`（本机现状） | 443 被阻断，必须走代理 |
| `git config --get http.proxy` | 空 = 没配代理 | 需要用 `-c` 显式指定 |
| `git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main` | 打印一行 SHA | ✅ **只读测通** |
| `git fetch origin --prune` | 退出码 0 | 通道可用，且刷新远端记录 |
| `git rev-list --count origin/main..HEAD` | 数字 | **待推送的提交数** |
| `git rev-list --count HEAD..origin/main` | **必须为 0** | >0 要先 `rebase` |
| `ssh -T git@github.com` | `Hi ...!` | SSH 认证可用 |
| `cmdkey /list` | `git:https://github.com` | 凭据存在，推送不会要登录 |

---

## 11. 哪些东西**不在**这个仓库里（别去找）

| 东西 | 在哪 | 会不会被提交 |
|---|---|---|
| `res.pak`（打包产物） | 构建时自动装到 `coremod\mods\<ModName>\` | ❌ 不进仓库 |
| `bin/` `obj/`（编译产物） | 各 mod 目录下 | ❌ 被 `.gitignore` 排除 |
| `data.cdb` / `_diffCDB.pak`（数据补丁中间产物） | 各 mod 工程目录下 | ❌ 被忽略，由 `patch_*.py` 重新生成 |
| `coremod/config/*.json`（运行时配置） | **仓库外** | ❌ 永不提交（故意的） |
| `GamePseudocode/`（反编译代码） | 仓库根目录 | ❌ 未跟踪，**不要 add**（实测 **282 MB**） |
| `res/`（游戏全部资源） | 仓库根目录 | ❌ 未跟踪，**不要 add**（实测 **1.9 GB**） |
| 游戏本体、存档 | 游戏目录 | ❌ |

> 也就是说：**推送只影响源码和资源（图集 / 语音 / 文档）**，
> 不会碰你游戏里的配置和存档，推完不用重装模组。

---

## 附 A：完整流程（复制粘贴版）

```powershell
# 1. 进仓库
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"

# 2. 确认代理端口（换成实际值）
Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet

# 3. 只 stage 自己改的（以 ChronoBlade 为例）
git add ChronoBlade/
git status --short -- ChronoBlade/
git commit -m "本次改动说明"

# 4. 看清楚这次会推上去哪些提交（不是只有你刚提交的那个！）
git rev-list --count origin/main..HEAD
git log --oneline origin/main..HEAD

# 5. 推
git -c http.proxy=http://127.0.0.1:7897 push origin main

# 6. 验证
git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main
git rev-list --count origin/main..HEAD   # 应为 0
```

---

## 附 B：一键脚本 `push.ps1`

仓库根目录已经放了一个可直接用的 [`push.ps1`](./push.ps1)（**UTF-8 with BOM**，已过语法校验 + 冒烟测试）。

```powershell
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"

# 只提交并推送指定目录（推荐）
.\push.ps1 -Message "本次改动说明" -Paths DashOverhaul/

# 代理端口不是 7897 时手动指定
.\push.ps1 -Message "本次改动说明" -Paths ChronoBlade/ -Port 7890

# 多个目录
.\push.ps1 -Message "本次改动说明" -Paths DashOverhaul/,PUSH_GUIDE.md
```

它依次做：**检查代理端口** → 只 stage 指定路径 → 提交 → **打印将要推送的提交列表**（防止连带推上积压提交）→ 推送 → 校验远端 SHA。

> ⚠️ **自己另存 `.ps1` 时必须用「UTF-8 with BOM」。**
> 本机是 **Windows PowerShell 5.1**，它读取**没有 BOM 的 UTF-8 脚本时会按 GBK 解析**：
> 脚本里的中文全变乱码，甚至报 `数组索引表达式丢失或无效`、`意外的标记 }` 这类看着莫名其妙的语法错误。
> （实测：同一份脚本，无 BOM → 报语法错；加 BOM → 完全正常。）
> VSCode 里存的时候右下角编码选 **`UTF-8 with BOM`**。本仓库的 `push.ps1` 已经是带 BOM 的，别用记事本"另存为 UTF-8"覆盖它。

<details>
<summary>脚本源码（想自己存一份时展开）</summary>

```powershell
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
```

</details>

---

## 附 C：成功案例存档

### 2026-09-30 —— DashOverhaul 推送

```
a7f77ef..9e38382  main -> main
```

- 命令：`git -c http.proxy=http://127.0.0.1:7897 push origin main`
- 结果：远端 `main` = `9e38382`，本次带上 21 个积压的 ChronoBlade 提交 + 1 个 DashOverhaul 提交，共 **22 个**。
- 经验：**先跑 `git log --oneline origin/main..HEAD`**，不然不会知道连带推上去这么多。

### 当前待推送状态

**不要记数字**（写本文档时本身也会产生待推送提交）。现查：

```powershell
git rev-list --count origin/main..HEAD   # 待推送数
git log --oneline origin/main..HEAD      # 具体是哪几笔
```

写这份文档时，待推送的就是 `PUSH_GUIDE.md` / `push.ps1` 这几笔 —— 推一次即同步。
