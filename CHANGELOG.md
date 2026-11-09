# 更新日志

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-11-09

首个公开版本。

### 新增

- 勾选式启动列表：一个勾选框一批程序，左侧程序列表支持搜索和分组筛选
- **每个程序独立间隔**：启动后等待 N 秒再启动下一个，串行调度，不抢磁盘和 CPU
- 手动拖入：`.lnk` / `.exe` / 文件夹 / `.url` / `.bat` / 任意文档；空列表时有虚线拖放区提示
- 启动引擎：`CreateProcess` 原生启动（不经过 cmd），提权 / 文件夹 / 网址 / 文档走 `ShellExecuteEx`
- `.lnk` 解析：二进制解析为主、`WScript.Shell` COM 兜底；**启动时优先直接 ShellExecute 原快捷方式**，
  保留参数 / 起始位置 / AppUserModelID
- 需要管理员的程序自动改用 `runas` 提权（错误码 740 → UAC）
- 已在运行时跳过，不重复开第二个（完整路径 + 进程名两级判定，兼容托盘里的实例）
- 启动后存活检查：进程秒退时把**退出码**写进日志；退出码非 0 时自动改用 `ShellExecute` 重试
- 分组：日常 / 工作 / 系统工具，底栏可单独启动某一组
- 深色界面：自绘按钮、开关、滚动条、列表行，150% DPI 下文字原生清晰（PerMonitorV2）
- 日志：界面实时显示 + 后台线程写 `logs\launch-yyyy-MM-dd.log`，按天切分
- 配置：`%APPDATA%\QuickLaunch\config.json`，原子写入 + 自动备份，不可写时退回 exe 同级目录
- 诊断模式：`--selftest` / `--uicheck` / `--launch-test` / `--launch-one` / `--savetest`，
  附带控制台版 `QuickLaunchDiag.exe` 和 UTF-8 报告文件
- 图标：脚本生成 7 套方案（`tools/make-icons.py`），`编译.cmd -Icon N` 一键切换

### 修复

- 关闭窗口时 `OnFormClosing` 自己调自己导致 StackOverflow
- 列表行高不足导致整行空白（从 `ListView` 改成自绘列表）
- 高 DPI 下文字发虚（补 `app.manifest` 的 DPI 感知声明）
- 右侧面板子控件未按 DPI 缩放，150% 下文字溢出、控件挤在左上角
- 图标字形用界面字体绘制导致**只有图标的按钮整组消失**（右上角最小化/最大化/关闭）
- 手动 spawn `explorer.exe` 导致部分系统报 `0xc0000142`（改为把文件夹路径交给 shell）
- `ShellExecuteEx` 失败时读错错误来源（应取 `hInstApp` 的 `SE_ERR_*`，不是 `GetLastWin32Error`）
- 测试会覆盖用户真实配置（`--savetest` / `LayoutTest` / `Shot` 全部隔离到临时目录）
- 工具列按钮宽度按单一字体测量导致文字被截断

### 已知问题

- 个别程序（实测 Obsidian 1.14.4）拒绝被「程序」启动：进程瞬间退出、退出码 0、
  连它自己的日志都不写，但鼠标双击快捷方式正常。属于该程序自身的启动守卫。
- 跨不同缩放比例的显示器拖动窗口时不会自动重新缩放（.NET Framework 4.8 WinForms 固有短板）
