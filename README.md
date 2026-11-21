# ChaoxingAutoPlayer（超星视频连播器）

这是从 SelfDiscipline 仓库中独立出来的完整 ChaoxingAutoPlayer 源码项目。它不引用 SelfDiscipline 的工程，也不要求安装 SelfDiscipline，可以单独编译、测试和发布。

功能细节、使用方式和本地数据位置见 [应用说明](src/ChaoxingAutoPlayer/README.md)。

## 项目结构

```text
chaoxingautoplayer/
├─ src/ChaoxingAutoPlayer/          主程序源码、资源和应用说明
├─ tests/ChaoxingAutoPlayer.Tests/  自动化测试
├─ installer/                       Inno Setup 安装包脚本
├─ tools/                           图标生成与浏览器调试工具
└─ ChaoxingAutoPlayer.sln           独立 Visual Studio / Rider 解决方案
```

原仓库中的 `bin`、`obj`、构建产物和本机登录状态都没有复制进来。程序运行时的设置、课程地址和 WebView2 会话仍保存在 `%LOCALAPPDATA%\ChaoxingAutoPlayer`，不会被 Git 上传。

## 开发环境

- Windows 10/11 x64
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime
- Inno Setup 6（仅制作安装包时需要）

## 编译与测试

在 JetBrains Rider 中打开根目录的 `ChaoxingAutoPlayer.sln`，或在 IDE 集成终端运行：

```powershell
dotnet restore .\ChaoxingAutoPlayer.sln
dotnet build .\ChaoxingAutoPlayer.sln -c Debug
dotnet test .\tests\ChaoxingAutoPlayer.Tests\ChaoxingAutoPlayer.Tests.csproj -c Debug
```

## 发布便携版

项目已配置为 Windows x64 自包含单文件发布：

```powershell
dotnet publish .\src\ChaoxingAutoPlayer\ChaoxingAutoPlayer.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\artifacts\ChaoxingAutoPlayer\portable-0.1.4
```

生成的可执行文件位于 `artifacts\ChaoxingAutoPlayer\portable-0.1.4\ChaoxingAutoPlayer.exe`。这个目录不会提交到 Git，适合作为 GitHub Release 附件上传。

## 制作安装包（可选）

先完成上面的便携版发布，再使用 Inno Setup 6 编译：

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
  .\installer\ChaoxingAutoPlayer.iss
```

安装包会输出到 `artifacts\ChaoxingAutoPlayer`。如果 Inno Setup 安装在其他位置，请替换可执行文件路径。

## 重新生成图标

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\make-chaoxing-logo.ps1
```

请遵守课程平台、学校和教师的相关使用规定。
