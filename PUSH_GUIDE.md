# 推送指南（DCCM 仓库 → GitHub）

> 仓库路径：`D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS`
> 远端仓库：<https://github.com/ZHIZHU4410/DCCM>
> 本文档中标 ✅ 的都是**实测过**的结果（最后验证：2026-09-30）。

---

## 0. 现在就能推的一条命令

代理客户端开着（本机当前监听 **7897**）的情况下，直接用这条：

```powershell
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"

git -c http.proxy=http://127.0.0.1:7897 -c https.proxy=http://127.0.0.1:7897 push origin main
```

✅ 2026-09-30 实测：`fetch` 与 `ls-remote` 走这条通道都成功，且
「领先 origin/main 22 个提交、落后 0 个」→ 是一次干净的 fast-forward，不会被拒。

> ⚠️ **必须是 `push origin main`，不能只写 `git push`。**
> 这个分支跟踪的是 **`upstream`（SSH 那个远端）**，裸 `git push` 会走 SSH，
> 而本机没有 SSH 私钥 → 直接 `Permission denied (publickey)`。详见第 1 节。

---

## 1. 这个仓库的现状（理解这些就不会踩坑）

| 项目 | 值 | 说明 |
|---|---|---|
| 当前分支 | `main` | |
| `origin` | `https://github.com/ZHIZHU4410/DCCM.git` | HTTPS。**推送用这个** |
| `upstream` | `git@github.com:ZHIZHU4410/DCCM.git` | SSH，**同一个仓库**，只是协议不同 |
| `branch.main.remote` | **`upstream`** | ⚠️ 所以裸 `git push` 走 SSH，会失败 |
| 提交身份 | `BLF4410 <1815130890@qq.com>` | |
| git 版本 | `2.53.0.windows.2` | |
| ssh 版本 | `OpenSSH_for_Windows_9.5p2` | |
| `~/.ssh` 内容 | **只有 `known_hosts`** | ⚠️ 没有私钥 → SSH 推送必失败 |
| git 代理配置 | **全局和本地都是空的** | 所以必须每次用 `-c` 显式指定，见第 3 节 |

### 两个远端的关系

两个远端指向**同一个 GitHub 仓库**，只是协议不同：

```
origin    https://github.com/...   ← 走 443，需要代理（被 SNI 阻断）
upstream  git@github.com:...       ← 走 22 / 443，不需要代理，但需要密钥
```

`upstream/main` 本地记录停在 `a084a83`，那是**很早以前的一次 fetch 记录**
（已确认它是 `origin/main` 的祖先，不影响任何事）。**以 `origin/main` 为准。**

---

## 2. 为什么平时推不上去（诊断结论）

✅ 实测的三条事实：

1. **DNS 是好的**：`github.com` → `20.205.243.166`，是 GitHub 真实 IP（不是 DNS 污染）。
2. **`github.com:443` 被按域名（SNI）阻断**：
   - 直连 `git ls-remote` → `Recv failure: Connection was reset`
   - 但 `ssh.github.com:443` 和 `github.com:22` 都通 —— 说明掐的是「这个域名 + 这个端口」。
3. **本机没有在跑的代理时，任何直连 HTTPS 都会失败**。

所以结论是：**要么开代理走 HTTPS，要么配 SSH 密钥走 SSH（可走 443 绕开）**，
没有第三条"直连就能通"的路。

### 关于代理端口：**它会变，别写死**

| 时间 | 实际监听的端口 |
|---|---|
| 之前 | `7890`（配置里曾写死过这个） |
| 现在 ✅ | **`7897`** |

老配置写的是 `7890`，而现在客户端用的是 `7897`（Clash Verge 默认混合端口）——
**这就是之前"明明开了代理还是推不上去"的真正原因**。每次推之前先确认端口。

---

## 3. 路线 A：走代理推（最快，推荐日常用）

### 步骤

1. **打开代理客户端**，确认它监听哪个端口（Clash 类客户端在界面或设置里能看到"混合端口"）。
2. **确认端口真的在监听**：

```powershell
Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet
# True = 在监听；False = 客户端没开或端口不对
```

3. **推送**（把端口换成你实际的）：

```powershell
git -c http.proxy=http://127.0.0.1:7897 -c https.proxy=http://127.0.0.1:7897 push origin main
```

### 想省掉每次输 `-c`？

可以写进**本仓库**配置（推荐局部，不动全局）：

```powershell
# 设置（只影响这个仓库）
git config http.proxy  http://127.0.0.1:7897
git config https.proxy http://127.0.0.1:7897

# 以后直接： git push origin main
```

```powershell
# 取消（代理关了以后一定要取消，否则连 git status 之类都可能变慢）
git config --unset http.proxy
git config --unset https.proxy
```

> ⚠️ **不建议写进 `--global`**：端口会变（见第 2 节），写死之后
> 下次客户端换端口，你会得到 `Failed to connect to 127.0.0.1 port 7890`，
> 而且那个报错看起来像"网络坏了"，其实只是端口不对。
>
> ⚠️ 用 TUN / 系统代理模式时，git 也可能**不配代理就能直连**；
> 判断方法就是先试第 0 节那条 `-c` 命令，能过就说明通道是通的。

---

## 4. 路线 B：SSH over 443（不需要代理，一次配好永久可用）

适合「不想每次开代理」的情况。`ssh.github.com:443` ✅ 实测可达。

### 步骤

1. **生成密钥**（本机目前没有私钥，这一步是必须的）：

```powershell
ssh-keygen -t ed25519 -C "1815130890@qq.com"
# 一路回车即可（想设密码也行）
```

2. **把公钥加到 GitHub**：打开 `~/.ssh/id_ed25519.pub`，复制**全部内容**，
   到 GitHub → Settings → SSH and GPG keys → New SSH key 粘贴保存。

3. **写 SSH 配置**，让 `github.com` 走 443 端口
   （文件：`C:\Users\10113\.ssh\config` —— **目前不存在，需要新建**）：

```
Host github.com
  HostName ssh.github.com
  Port 443
  User git
  IdentityFile ~/.ssh/id_ed25519
```

4. **测试认证**：

```powershell
ssh -T git@github.com
# 成功会显示： Hi ZHIZHU4410! You've successfully authenticated...
# 失败是：     git@github.com: Permission denied (publickey)
```

5. **推送**：因为分支跟踪的就是 `upstream`（SSH），此时裸 `git push` 也能用了；
   想明确指定就：

```powershell
git push upstream main
```

### 顺带把跟踪关系改回 origin（可选）

如果更喜欢 `git push` 就走 HTTPS：

```powershell
git branch -u origin/main
```

---

## 5. 路线 C：什么都不通时——打包带走

在能联网的机器上再推：

```powershell
git bundle create chronoblade.bundle origin/main..main
```

把这个 `.bundle` 文件拷到另一台机器，然后：

```powershell
git clone chronoblade.bundle myrepo
cd myrepo
git remote set-url origin https://github.com/ZHIZHU4410/DCCM.git
git push origin main
```

---

## 6. 诊断命令速查（出问题时按顺序跑）

| 命令 | 看什么 | 结论 |
|---|---|---|
| `git remote -v` | 两个远端的 URL | 确认推的目标是哪个 |
| `git config --get branch.main.remote` | `upstream` | **说明裸 `git push` 会走 SSH** |
| `Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet` | `True` | 代理在监听，端口记下来 |
| `Test-NetConnection github.com -Port 443 -InformationLevel Quiet` | `True`/`False` | 443 通不通 |
| `git config --get http.proxy` | 空 = 没配代理 | 需要用 `-c` 显式指定 |
| `git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main` | 打印出一行 SHA | ✅ **只读测通**，不推送也能验证通道 |
| `git fetch origin --prune` | 退出码 0 | 通道可用，且刷新远端记录 |
| `git rev-list --count origin/main..HEAD` | 数字 | **待推送的提交数** |
| `git rev-list --count HEAD..origin/main` | **必须为 0** | >0 说明远端有新提交，要先 `rebase` |
| `ssh -T git@github.com` | `Hi ...!` | SSH 认证可用 |

> 小技巧：`ls-remote` 是**只读**的，想确认"现在能不能推"而先不动远端，用它最合适。

---

## 7. 报错对照表（都是本项目真实遇到过的）

| 报错 | 真正原因 | 解决 |
|---|---|---|
| `Recv failure: Connection was reset` | HTTPS 直连被 SNI 阻断（或没走代理） | 加 `-c http.proxy=http://127.0.0.1:<端口>` |
| `Failed to connect to github.com port 443 after 21097 ms` | 连 TCP 都没通（代理没起 / 端口错） | 先按第 6 节确认代理和端口 |
| `Failed to connect to 127.0.0.1 port 7890` | **代理端口变了**（老配置写死 7890，实际是 7897） | 查实际端口，改 `-c` 里的端口 |
| `Permission denied (publickey)` | 裸 `git push` 走了 `upstream`(SSH)，而本机没有私钥 | 用 `git push origin main`，或按第 4 节配 SSH |
| `! [rejected] main -> main (non-fast-forward)` | 远端有你本地没有的提交 | `git fetch origin` 然后 `git rebase origin/main` 再推 |
| `MSB3021`（构建时） | 编译产物 dll 被占用（**游戏开着**） | 关掉游戏再 `dotnet build`；与推送无关 |
| `LF will be replaced by CRLF` | 换行符提示 | **不是错误**，忽略即可 |

---

## 8. 推送前的检查清单（这个仓库有坑，务必看）

### ⚠️ 最重要的坑：仓库里有很多**别人的/别的 mod** 的未提交改动

这是一个 DCCM 合集仓库，`git status` 会看到大量**与本次工作无关**的东西
（实测：6 个已修改文件 + 65 项未跟踪目录，属于 FlyingSword / Weaponbow /
BarrelLauncherOverhaul / ByCar 等其它 mod）。

**所以：绝对不要用 `git add -A` / `git add .` / `git commit -a`。**
只 stage 你这次真正改的路径：

```powershell
# 正确做法：只加自己那个 mod 的目录
git add ChronoBlade/
git status --short -- ChronoBlade/     # 复核一遍再提交
git commit -m "说明"
```

```powershell
# 提交前看清楚"到底会提交什么"
git diff --cached --stat
```

### 其它检查

```powershell
git rev-list --count origin/main..HEAD    # 待推送数（当前 22）
git rev-list --count HEAD..origin/main    # 必须 0
git status --short -- ChronoBlade/        # 自己那部分是否干净
```

### 大文件（GitHub 单文件上限 100 MB，超过 50 MB 会警告）

| 文件 | 大小 |
|---|---|
| `ChronoBlade/ChronoBlade/Assets/atlas/TIMEBEIJING.png` | 9.95 MB |
| `ChronoBlade/ChronoBlade/Assets/atlas/TIMEKASAN.png` | 3.99 MB |
| `ChronoBlade/ChronoBlade/Assets/sfx/kurumi01~08.WAV` | 合计约 6.5 MB |
| `ChronoBlade/ChronoBlade/Assets/cardIcons.png` | 1.39 MB |

都在限内 ✅。单次待推送体积约 16 MB（去重前粗算），正常几十秒内完成。

---

## 9. 推送后怎么确认成功

```powershell
# 1) 远端记录的 main 应该等于你本地 HEAD
git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main
git rev-parse HEAD

# 2) 待推送数应该变成 0
git rev-list --count origin/main..HEAD

# 3) 也可以直接看网页
start https://github.com/ZHIZHU4410/DCCM/commits/main
```

---

## 10. 哪些东西**不在**这个仓库里（别去找）

| 东西 | 在哪 | 会不会被提交 |
|---|---|---|
| `res.pak`（打包产物） | 由构建自动装到 `coremod\mods\ChronoBlade\` | ❌ 不进仓库 |
| `coremod/config/ChronoBlade.json`（运行时配置） | **仓库外** | ❌ 永不提交（故意的） |
| `bin/` `obj/`（编译产物） | 各 mod 目录下 | ❌ 被 `.gitignore` 排除 |
| 游戏本体、存档 | 游戏目录 | ❌ |

> 也就是说：**推送只影响源码和资源（图集 / 语音 / 文档）**，
> 不会碰你游戏里的配置和存档，推完不用重装模组。

---

## 附：完整流程（复制粘贴版）

```powershell
# 1. 进仓库
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS"

# 2. 确认代理端口（换成实际值）
Test-NetConnection 127.0.0.1 -Port 7897 -InformationLevel Quiet

# 3. 只 stage 自己改的（这里以 ChronoBlade 为例）
git add ChronoBlade/
git status --short -- ChronoBlade/
git commit -m "本次改动说明"

# 4. 看一眼待推送内容
git rev-list --count origin/main..HEAD
git log --oneline origin/main..HEAD

# 5. 推
git -c http.proxy=http://127.0.0.1:7897 -c https.proxy=http://127.0.0.1:7897 push origin main

# 6. 验证
git -c http.proxy=http://127.0.0.1:7897 ls-remote origin -h refs/heads/main
git rev-list --count origin/main..HEAD   # 应为 0
```
