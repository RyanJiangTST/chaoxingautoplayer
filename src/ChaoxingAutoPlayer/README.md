# 超星视频连播器

独立的 Windows 超星/泛雅课程视频连播工具。它不依赖 SelfDiscipline，其他同学可以复制便携目录或使用当前用户安装包直接运行。

## 使用

1. 打开 `ChaoxingAutoPlayer.exe`。
2. 粘贴当前课程的**第一个视频学习页 URL**，点击“打开课程”。
3. 在独立 WebView2 窗口中完成一次登录。
4. 等待右侧显示视频队列，点击“开始连播”。
5. 打开“设置”调整静音、光标离开行为和视频学习测验处理方式。开始/暂停按钮始终留在主窗口。

程序会按当前课程目录扫描章节和视频任务点，自动播放未完成视频，并把测验、文档和讨论等非视频任务列在右侧，供全部视频结束后统一处理。

扫描器同时支持旧版 `.ans-job-icon` 任务点和新版章节卡片中的 `iframe.ans-insertvideo-online` / `iframe.insertdoc-online-pdf`，因此不会把新课程的文档卡片误判成视频。更换课程网址时，如果更换前正在连播，程序会保留连播状态并在新页面扫描完成后自动继续。

部分课程把“完成前置测验”作为解锁下一章的条件。遇到这种顺序锁定时，程序会停止在锁定章节边界并提示先完成未完成项；章节测验本身不会被自动作答。

## 视频互动题

- 只处理视频播放中弹出的 `InteractiveQuiz`，不处理章节测验、作业或考试。
- “自动跳过学习测验”只会点击页面提供的关闭/跳过入口，不提交答案；没有可识别入口时会暂停。
- 页面公开正确选项标记时，优先选择全部正确项并提交。
- 没有正确标记时，“自动尝试”模式默认提交第 1 项，再提交第 2 项，之后按页面顺序尝试剩余单项选择。
- 首选、次选和最多尝试次数都可以在“设置 → 互动题”中修改。
- “暂停并提示”模式只暂停，用户可以在右侧状态区点击“尝试处理互动题”。
- 多选题没有正确标记、服务端禁止重试或全部选项失败时，程序暂停并等待手动处理。

## SelfDiscipline 锁屏联动

勾选“准备 SelfDiscipline 锁屏连播”并让本程序驻留托盘。SelfDiscipline 进入**全屏锁屏**后，程序会自动开始连播；锁屏结束时立即暂停并保存。

普通小窗和编程模式不会触发锁屏联动。Windows 自带锁屏也不会被误认为 SelfDiscipline 锁屏。

## 恢复与限制

- 每 5 秒保存当前视频和播放位置；正常重启后会尝试回到上次停止的视频。
- 播放速度固定为 1×，不修改平台播放进度接口。
- 实际系统睡眠会暂停并保存，唤醒后保持暂停。
- 静音使用 WebView2 浏览器级静音，同时持续压制网页重新开启媒体声音。
- 不锁定或移动系统鼠标，不安装低级鼠标钩子。
- 遇到验证码、无法解析的互动题、章节锁定或登录过期时会暂停。
- 验证 URL包含课程访问令牌，只保存在本机 `%LOCALAPPDATA%\ChaoxingAutoPlayer\state.json`，日志会自动脱敏。

## 环境

- Windows 10/11 x64。
- Microsoft Edge WebView2 Evergreen Runtime。Windows 11 通常已自带；缺失时程序会给出提示。
- 便携版不需要安装 .NET。

## 目录

- 状态：`%LOCALAPPDATA%\ChaoxingAutoPlayer\state.json`
- 设置：`%LOCALAPPDATA%\ChaoxingAutoPlayer\settings.json`
- 浏览器会话：`%LOCALAPPDATA%\ChaoxingAutoPlayer\WebView2`
- 日志：`%LOCALAPPDATA%\ChaoxingAutoPlayer\logs\app.log`

Logo 源文件和 Windows ICO 位于 `assets\branding`。0.1.5 保留 `IInteractiveQuizSolver` 接口；未来的 1.x AI 版本可以在不改变浏览器传输层的情况下替换为在线模型求解器。

请遵守课程和学校的相关使用规定。
