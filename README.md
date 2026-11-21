<div align="center">

<img src="src/ChaoxingAutoPlayer/assets/branding/logo-256.png" width="112" alt="超星视频连播器图标">

# Chaoxing AutoPlayer

**超星视频连播器**

面向 Windows 的超星 / 泛雅课程视频连播工具。它使用独立 WebView2 窗口打开课程网页，扫描章节与视频任务点，并辅助完成未播放视频的连续播放。

[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Windows x64](https://img.shields.io/badge/Windows-10%2F11%20x64-0078D4?logo=windows&logoColor=white)](#系统要求)
[![WebView2](https://img.shields.io/badge/Browser-WebView2-0CAA41?logo=microsoftedge&logoColor=white)](https://developer.microsoft.com/microsoft-edge/webview2/)

[功能](#功能概览) · [快速开始](#快速开始) · [设置](#设置说明) · [常见问题](#常见问题) · [源码构建](#从源码构建)

</div>

> [!IMPORTANT]
> 请仅播放本人有权访问的课程内容，并遵守课程平台、学校及教师的使用规定。本工具不会自动作答章节测验、作业或考试；遇到验证码、登录失效或需要人工判断的页面时，应主动接管处理。

## 项目简介

Chaoxing AutoPlayer 是一个独立的 Windows 桌面程序，不修改超星平台的播放进度接口。程序将课程页面嵌入 WebView2，并在本地完成以下工作：

- 从课程目录中识别章节和视频任务点；
- 按顺序播放尚未完成的视频，并在视频结束后切换任务；
- 把测验、文档、讨论等非视频任务列在右侧，避免误判为视频；
- 保存当前视频与播放位置，重启后可继续上次进度；
- 在需要人工处理的场景暂停，而不是持续盲目操作。

项目当前源码版本为 **0.1.4**，使用 C# / WPF 与 Microsoft WebView2 构建。

## 功能概览

| 功能 | 说明 |
| --- | --- |
| 课程任务扫描 | 支持旧版 `.ans-job-icon` 任务点以及新版章节卡片中的视频、文档 `iframe`。 |
| 视频连续播放 | 自动选择待播放视频，结束后继续队列或寻找后续章节。 |
| 非视频任务提示 | 将章节测验、文档、讨论等任务显示在右侧，供播放结束后统一处理。 |
| 断点恢复 | 每 5 秒保存当前视频和播放位置；重新打开后尝试回到上次停止的位置。 |
| 光标行为控制 | 可选择光标离开网页时暂停，或在不移动系统鼠标的情况下继续播放。 |
| 视频互动题辅助 | 可自动尝试视频播放中弹出的互动题，也可暂停后由用户手动处理。 |
| 静音与托盘运行 | 支持浏览器级静音，并可隐藏到系统托盘继续运行。 |
| SelfDiscipline 联动 | 可选在 SelfDiscipline 全屏锁屏期间自动开始连播，锁屏结束后暂停并保存。 |

程序不会锁定或移动系统鼠标，也不会安装低级鼠标钩子。播放速度固定为 **1×**，不会通过修改平台接口跳过视频。

## 系统要求

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 10 / 11，x64 |
| 运行时 | Microsoft Edge WebView2 Evergreen Runtime |
| 安装版 | 无需单独安装 .NET |
| 便携版 | 无需单独安装 .NET，解压后运行即可 |
| 从源码构建 | .NET 8 SDK；制作安装包时另需 Inno Setup 6 |

Windows 11 通常已经包含 WebView2 Runtime。如果启动时提示缺少运行时，请从 [Microsoft WebView2 官网](https://developer.microsoft.com/microsoft-edge/webview2/) 安装 **Evergreen Bootstrapper** 或 **Evergreen Standalone Installer**。

## 下载与运行

请从 [GitHub Releases](https://github.com/RyanJiangTST/chaoxingautoplayer/releases) 获取公开发布包：

- `ChaoxingAutoPlayer-Setup-<版本>.exe`：当前用户安装版，会创建开始菜单和可选的桌面快捷方式；
- `portable-<版本>`：便携版目录，运行其中的 `ChaoxingAutoPlayer.exe`。

如果 Releases 中暂时没有可用构建，可按照[从源码构建](#从源码构建)自行生成。

## 快速开始

1. 启动 `ChaoxingAutoPlayer.exe`。
2. 在顶部粘贴一个 `https://*.chaoxing.com` 课程视频学习页地址，点击 **打开课程**。建议从课程的第一个视频开始，也可以使用其他视频学习页。
3. 首次使用时，在程序内的 WebView2 窗口中完成登录。登录状态会保存在本机，后续通常无需重复登录。
4. 等待右侧显示任务扫描结果。若页面没有及时更新，可点击 **重新扫描**。
5. 点击 **开始连播**。程序会从当前课程目录中选择待播放视频，并在视频结束后继续后续任务。
6. 如果右侧出现待处理的互动题、章节锁定或登录提示，请根据状态文字手动处理；处理完成后可再次点击 **开始连播**。

关闭主窗口时程序会隐藏到系统托盘，而不是立即退出。需要彻底结束时，在托盘图标菜单中选择 **退出**。

## 设置说明

点击主窗口中的 **设置** 调整以下选项。保存后对后续播放生效，开始和暂停仍由主窗口控制。

| 分类 | 默认值 | 可调整内容 |
| --- | --- | --- |
| 常规 | 静音播放 | 是否默认静音；是否启用 SelfDiscipline 全屏锁屏联动。 |
| 光标 | 光标离开后继续播放 | 可选择离开网页即暂停，或继续播放但不移动系统鼠标。 |
| 互动题 | 自动尝试 | 可选择“暂停并提示”或“自动选择并提交”。 |
| 尝试顺序 | 第 1 项、第 2 项，再按页面顺序 | 可设置首选、次选，以及失败后是否尝试剩余选项。 |
| 尝试次数 | 最多 4 次 | 可设置为 1–4 次。 |

### 视频互动题的处理边界

- 只处理视频播放中弹出的 `InteractiveQuiz`，不处理章节测验、作业或考试。
- 页面公开正确选项标记时，优先选择全部正确项并提交。
- 没有正确标记且启用自动尝试时，按“首选 → 次选 → 其余选项”的顺序提交；多选题不会在没有正确标记时随机组合答案。
- 服务端禁止重试、全部选项失败或需要人工判断时，程序暂停并等待用户处理。
- 选择“暂停并提示”后，可在右侧状态区点击 **尝试处理互动题**。

## SelfDiscipline 锁屏联动

勾选 **SelfDiscipline 全屏锁屏时自动开始连播** 后，让本程序驻留系统托盘。SelfDiscipline 进入全屏锁屏时，程序会开始连播；锁屏结束时，程序会立即暂停并保存当前状态。

该联动只响应 SelfDiscipline 的**全屏锁屏状态**。普通小窗、编程模式以及 Windows 自带的锁屏不会触发自动播放。

## 状态恢复与暂停条件

程序会在以下场景主动暂停或停止继续推进：

- 光标离开课程网页，且当前设置为“离开后暂停”；
- 系统进入睡眠；
- 登录失效、出现验证码或页面要求人工确认；
- 后续章节被前置测验或未完成任务锁定；
- 互动题无法确定答案、达到最大尝试次数或平台禁止重试；
- 无法继续切换视频或页面结构发生不兼容变化。

系统唤醒后，程序保持暂停；确认页面状态无误后再手动点击 **开始连播**。

## 本地数据与隐私

程序不包含遥测上传。课程访问令牌、登录会话、设置和日志仅保存在当前 Windows 用户的本机目录中。

| 内容 | 默认位置 |
| --- | --- |
| 运行数据根目录 | `%LOCALAPPDATA%\ChaoxingAutoPlayer` |
| 播放断点与课程地址 | `%LOCALAPPDATA%\ChaoxingAutoPlayer\state.json` |
| 程序设置 | `%LOCALAPPDATA%\ChaoxingAutoPlayer\settings.json` |
| WebView2 登录会话 | `%LOCALAPPDATA%\ChaoxingAutoPlayer\WebView2` |
| 应用日志 | `%LOCALAPPDATA%\ChaoxingAutoPlayer\logs\app.log` |

课程 URL 可能包含访问令牌，因此这些文件不应上传到 Git、网盘或公开问题中。程序写入日志时会自动遮蔽 `enc`、`openc`、`token`、`ticket`、`sign` 和 `ak` 等查询参数，但提交问题前仍建议检查日志内容。

## 常见问题

### 启动时提示缺少 WebView2

安装 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)，然后重新启动程序。Windows 11 已预装时，请检查系统更新或尝试 Evergreen Standalone Installer。

### 粘贴网址后无法打开

程序只接受 `https://chaoxing.com` 或 `https://*.chaoxing.com` 地址。请确认使用的是课程学习页，而不是 `http` 页面、其他站点或缺少协议头的地址。

### 页面已打开，但没有扫描到视频

先在嵌入式浏览器中完成登录和目标课程加载，再点击 **重新扫描**。如果仍然没有任务，可能是课程页面结构发生变化；请保留复现步骤并附上脱敏后的 `app.log`。

### 视频结束后停在某一章

后续章节可能被前置测验、文档或其他任务锁定。查看右侧 **已跳过的非视频任务**，在平台中手动完成阻碍解锁的任务，然后重新扫描并继续。程序不会自动完成这些任务。

### 互动题没有自动提交

自动尝试只适用于页面能够解析的单选题式尝试。多选题没有公开答案、服务器禁止重试或全部选项失败时，程序会暂停，这是为了避免无效提交。请切换到“暂停并提示”模式，必要时手动完成。

### 登录过期或页面跳回登录页

在嵌入式浏览器中重新登录，确认课程页面恢复后，点击 **重新扫描** 和 **开始连播**。如反复失效，请先退出所有程序实例并删除 `%LOCALAPPDATA%\ChaoxingAutoPlayer\WebView2` 后重新登录；该操作会清除本机 WebView2 会话。

## 从源码构建

### 环境准备

- 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)；
- 安装 Microsoft Edge WebView2 Runtime；
- 使用 JetBrains Rider 打开根目录的 `ChaoxingAutoPlayer.sln`，或在 IDE 集成终端中执行以下命令。

### 还原、编译与测试

```powershell
git clone https://github.com/RyanJiangTST/chaoxingautoplayer.git
Set-Location .\chaoxingautoplayer

dotnet restore .\ChaoxingAutoPlayer.sln
dotnet build .\ChaoxingAutoPlayer.sln -c Debug
dotnet test .\tests\ChaoxingAutoPlayer.Tests\ChaoxingAutoPlayer.Tests.csproj -c Debug
```

### 发布 Windows x64 便携版

```powershell
dotnet publish .\src\ChaoxingAutoPlayer\ChaoxingAutoPlayer.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\artifacts\ChaoxingAutoPlayer\portable-0.1.4
```

生成的可执行文件位于：

```text
artifacts\ChaoxingAutoPlayer\portable-0.1.4\ChaoxingAutoPlayer.exe
```

发布目录已配置为自包含单文件输出，不会提交到 Git，适合作为 GitHub Release 附件。

### 制作 Inno Setup 安装包

先完成便携版发布，再安装 Inno Setup 6 并编译：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  .\installer\ChaoxingAutoPlayer.iss
```

安装包默认输出到 `artifacts\ChaoxingAutoPlayer`。如果 Inno Setup 安装在其他位置，请替换上面的可执行文件路径。

### 重新生成图标

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\make-chaoxing-logo.ps1
```

## 项目结构

```text
chaoxingautoplayer/
├─ src/ChaoxingAutoPlayer/           主程序、WebView2 桥接与界面
│  ├─ Automation/                    页面扫描、播放控制和互动题处理
│  ├─ Models/                        设置与播放断点模型
│  ├─ Services/                      本地存储、日志和 URL 校验
│  └─ assets/branding/               应用图标与 Logo 源文件
├─ tests/ChaoxingAutoPlayer.Tests/   核心行为测试
├─ installer/                        Inno Setup 安装脚本
├─ tools/                            Logo 生成和浏览器调试工具
├─ artifacts/                        本地构建产物，不提交 Git
└─ ChaoxingAutoPlayer.sln            JetBrains Rider / .NET 解决方案
```

## 问题反馈

提交 Issue 时请提供：

- Windows 版本和程序版本；
- 问题发生的操作步骤与页面状态；
- 如果可以，附上复现过程截图；
- 脱敏后的 `%LOCALAPPDATA%\ChaoxingAutoPlayer\logs\app.log`。

请勿上传包含课程访问令牌、账号信息、Cookie 或完整私有课程地址的内容。

## 使用声明与许可证

本项目用于辅助播放已获授权访问的课程视频，不保证与所有学校、课程版本或超星页面结构兼容。平台页面更新可能导致扫描失效，遇到重要课程或考试场景时请自行检查并手动操作。

仓库目前未附带独立开源许可证。如需复制、修改、分发或用于其他项目，请先联系仓库作者确认授权。
