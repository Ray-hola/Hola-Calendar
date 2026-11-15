# 哗啦待办 · 桌面伴侣（macOS）

菜单栏小应用：把网页版「桌面小组件」搬到 Mac 上——今日待办、25/5 番茄钟、刘海「灵动岛」、Touch Bar，开机自启，两种风格（Anthropic风 / Tempo风）。原生 Swift，不装浏览器内核，常驻约 45 MB。

调研与选型见 [RESEARCH.md](RESEARCH.md)。

## 目录

```
desktop/
  README.md / RESEARCH.md
  macos/
    Package.swift                 SwiftPM 清单（工具链健康时可用 swift build / swift run）
    Sources/HolaCore/             纯 Foundation：模型、API 客户端、Keychain、番茄钟状态机、今日筛选
    Sources/HolaDesktop/          菜单栏应用：弹窗、主题、吉祥物、Touch Bar、刘海岛、开机自启
    Sources/holactl/              命令行客户端（排障、集成测试用）
    Tests/HolaCoreTests/          单元测试（自带极简断言，不依赖 XCTest）
    Resources/Info.plist
    scripts/build.sh              只用 swiftc 构建并打包 .app（无需 Xcode）
    scripts/make-icon.sh          由 assets/logo/icon-sweep.svg 生成 .icns
    scripts/integration-test.sh   起一个临时 server.py 跑端到端
```

## 构建与运行

需要 macOS 13+、Apple Silicon 或 Intel、Command Line Tools（`xcode-select --install`），**不需要 Xcode**。

```bash
cd desktop/macos
scripts/build.sh            # 产出 build/哗啦待办.app（ad-hoc 签名）
scripts/build.sh test       # 单元测试
scripts/build.sh run        # 构建并打开
scripts/integration-test.sh # 端到端：临时起 server.py，登录、列今日、完成、核对 403/409
```

想常驻使用：把 `build/哗啦待办.app` 拖到「应用程序」再打开（SMAppService 开机自启在固定路径下最可靠）。

脚本会自动挑一个与编译器匹配的 SDK；若机器上 CLT 的 SDK 与编译器版本不一致（本机就是：编译器 6.3.3，默认 SDK 为 6.4 构建），它会退回到可用的那个。`SDKROOT` 可手动指定。

工具链健康时也可以：`swift build -c release`、`swift run HolaCoreTests`、`swift run holactl today 192.168.1.8:4173`。

## 登录与同步

1. 点菜单栏的「三张卡」图标，填服务器地址（如 `http://192.168.1.8:4173`，不写协议默认 http）、账户、密码，回车。
2. 应用调用网页同一个 `POST /api/login`，拿到 `wc_session` 后**只存进系统钥匙串**（不写文件、不留 Cookie）。
3. 之后每 45 秒拉一次 `/api/state`，筛出今天、属于你的任务（规则与网页小组件一致）；打开弹窗若超过 10 秒未同步会立刻刷新；合盖休眠时停表，唤醒立刻同步。
4. 勾一项 = 网页小组件同样的 `task.complete`（总结「完成任务」、实际用时取计划时长、感受「顺利」）。服务器拒绝（别人的任务、管理员账号、已记录过）时勾会弹回，原文显示服务器的说明。
5. 服务器连不上时保留上次列表，顶部显示「离线 · 上次同步 HH:mm」；401 视作会话失效，清钥匙串回到登录页。

## 番茄钟

25 分钟专注 / 5 分钟休息，开始 / 暂停 / 重置 / 切换，和网页版一致：到点自动切到另一模式（暂停态）并响一声。状态存 UserDefaults，重启不丢；运行中菜单栏图标旁显示倒计时。

## 「灵动岛」

Mac 没有真正的灵动岛。番茄钟运行时，屏顶中央出现一个黑色小岛：

- 有刘海的机型：包住刘海，左翼倒计时 + 吉祥物，右翼进度（Anthropic风是赭色圆环，Tempo风是 `[##....]`），与刘海融为一体；
- 无刘海的机型 / 外接屏：菜单栏下方一枚圆角药丸；
- 悬停展开：显示当前待办、进度条；点一下 = 暂停 / 继续；
- 跟随鼠标所在的屏幕，接拔显示器自动重算；番茄钟停止后隐藏。

真正的 iPhone 灵动岛需要 iOS App + ActivityKit，见 RESEARCH.md。

## Touch Bar

应用在前台（弹窗打开）时，Touch Bar 显示：开始/暂停、倒计时、切换模式、下一项待办。没有 Touch Bar 的机器（如本机）什么都不会发生。常驻 Control Strip 需要私有 API，没做。

## 开机自启

弹窗底部「登录时启动」= `SMAppService.mainApp.register()`（macOS 13+），系统设置 → 通用 → 登录项里可见、可关。直接运行裸可执行文件（非 .app）时系统会拒绝，弹窗会显示原因。

## 两种风格

- **Anthropic风**：米白纸色、赭红、衬线标题；吉祥物「小卡」——logo 里那张卡片长出了脸：专注时蹦跳、休息时闭眼冒 z、待机时慢慢呼吸。**没有使用 Anthropic 的 Claude 标志或任何官方角色**（那是 Anthropic 的商标，这个应用要发给用户），小卡是品牌自己的卡片。
- **Tempo风**：等宽字、`[ 开始 ]` 方括号按钮、`== 标题 ==`、`[####......] 40%` ASCII 进度、`[ ]`/`[x]` 复选；像素动画为原创代码绘制（专注：沙漏流沙 4 帧；休息：冒气咖啡杯 3 帧；待机：沙粒眨眼），没有复制任何网站的像素素材。

## 已验证 / 待人工验证

本机只有 Command Line Tools，且 SwiftPM 二进制与其框架版本不匹配，所以用 `scripts/build.sh` 直接调 swiftc。已验证：

- `scripts/build.sh test`：85 项断言全部通过（番茄钟状态机、今日筛选、API 请求头/Cookie/错误映射、JSON 宽松解码）。
- `scripts/build.sh`：0 警告，.app 签名通过；启动 5 秒无崩溃，常驻 44 MB；写入运行中的番茄钟状态再启动（岛窗口 + 每秒 tick + Tempo风）也稳定，54 MB、CPU 0.1%。
- `scripts/integration-test.sh` 在开发机上**未能跑通**：该机此刻任何到本机 127.0.0.1 的 TCP 连接都超时（连 `nc` 对 `nc` 都不通，疑为 Clash Verge 的 TUN/系统扩展状态），不是应用的问题。请在网络正常的机器上跑一次。

需要人试：弹窗登录与勾选；刘海岛的外观与悬停展开；外接屏切换；Touch Bar（需要 2016–2019 MacBook Pro）；开机自启开关（把 .app 放进「应用程序」后试）；两种风格的动画观感。

---

## English

A native Swift menu-bar companion for a self-hosted 哗啦待办 / Hola-Calendar server: today's to-dos synced from the server (tick to complete through the same `task.complete` action the web widget uses), a 25/5 pomodoro, a notch "island" (black pill wrapping the notch while a session runs, expands on hover; falls back to a pill under the menu bar on Macs without a notch), a Touch Bar strip when the app is frontmost, launch at login via `SMAppService`, and two styles (Anthropic风: paper/terracotta/serif with an original card mascot — not Anthropic's Claude mark; Tempo风: monospace, bracket buttons, ASCII progress, original pixel sprites).

**Build** (macOS 13+, Command Line Tools only, no Xcode needed):

```bash
cd desktop/macos
scripts/build.sh          # -> build/哗啦待办.app
scripts/build.sh test     # unit tests
scripts/integration-test.sh   # spins up a throwaway server.py and exercises login/list/complete/refusals
```

**Sign in**: click the menu-bar icon, enter the server URL (e.g. `http://192.168.1.8:4173`), account and password. The `wc_session` token goes to the Keychain only. Polling every 45 s, paused during sleep; offline keeps the last list; 401 signs out.

**Launch at login**: the checkbox in the popover footer (works once the app runs from a `.app` bundle, ideally in /Applications).
