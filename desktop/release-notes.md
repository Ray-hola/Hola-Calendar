哗啦待办桌面 App 0.2.0：第一次同时提供 macOS 和 Windows 版。两者功能相同，都能连接你们团队自己部署的哗啦待办服务器。

## 能做什么
- **今日待办**：同步今天分给你的任务，勾一下就记为完成，和网页版「桌面小组件」一样。
- **番茄钟**：25 分钟专注、5 分钟休息。Mac 在菜单栏显示倒计时，Windows 在托盘图标上显示剩余分钟数。
- **屏幕顶部的「岛」**：番茄钟运行时出现，悬停展开当前待办，点一下暂停或继续。Mac 有刘海时会包住刘海。
- **连接任意服务器**：填网址即可，或在网页版点头像 →「连接桌面 App」→「在桌面 App 中打开」。最多记住 8 台，一键切换。
- 开机 / 登录时启动，两种风格（Anthropic风 / Tempo风）。Mac 另有 Touch Bar。

## macOS
1. 下载 `Hola-Desktop-macOS-0.2.0-universal.zip`，解压得到 `哗啦待办.app`，拖进「应用程序」。
2. 这个版本没有 Apple 开发者签名和公证，第一次打开会被拦下：打开「系统设置 → 隐私与安全性」，在下方点「仍要打开」。也可以在终端运行：
   ```bash
   xattr -dr com.apple.quarantine /Applications/哗啦待办.app
   ```
3. 需要 macOS 13 或更新版本，Apple 芯片和 Intel 机型都能用。

## Windows
1. 下载 `Hola-Desktop-Windows-0.2.0.zip`，**先解压**到一个固定位置（例如「文档」或 `D:\Apps`），再双击 `哗啦待办.exe`。直接在压缩包里双击时，Windows 会把它放进临时文件夹运行，这时网页链接和开机自启都不会生效。
2. 这个版本没有代码签名，第一次打开 SmartScreen 可能提示「已保护你的电脑」：点「更多信息 → 仍要运行」。
3. 打开一次之后，网页上的「在桌面 App 中打开」就能找到它。图标在任务栏右下角的通知区域，可能需要点「^」展开。
4. 需要 Windows 10（1903 及以后）或 Windows 11，系统自带的 .NET Framework 4.8 即可，不用另装运行时。

## 说明
- 这是预发布版。两个版本都在 GitHub 的构建机上编译，单元测试和对真实 server.py 的端到端测试都通过后才打包，详见本次发布对应的 Actions 记录。
- 登录状态只存在系统钥匙串（Mac）或 Windows 凭据管理器里，不写入文件。
- 校验：见附件 `SHA256SUMS.txt`。
- 详细说明见仓库里的 [desktop/README.md](https://github.com/Ray-hola/Hola-Calendar/blob/main/desktop/README.md)。有问题请提 issue。
