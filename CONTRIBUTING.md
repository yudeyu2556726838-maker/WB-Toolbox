# Contributing to WB Toolbox

感谢反馈问题或参与改进。提交问题时，请尽量提供以下信息：

1. Windows 版本、屏幕缩放比例和显示器数量。
2. 使用的 WB Toolbox 版本，以及安装版或便携版。
3. 可稳定复现问题的最短步骤。
4. 实际结果、期望结果和相关截图。
5. 若发生崩溃，请附上 `%LOCALAPPDATA%\WBToolbox\logs` 中对应时间的日志；提交前请先移除个人路径等敏感信息。

项目处理问题时遵循 [用户反馈与问题处理方法](docs/用户反馈与问题处理方法.md)。代码修改在合入前应至少通过：

```powershell
.\Test-Native.ps1
.\Test-Translation.ps1
.\Build-VisualSmoke.ps1
```

发布构建还应运行：

```powershell
.\Build-Release.ps1
```

不要提交 `dist`、`release`、`work`、本地设置、日志或自定义背景文件。
