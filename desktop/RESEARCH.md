# 桌面伴侣调研 · Desktop companion research

日期 2026-11-15 · 机器：Mac17,3（Apple M5，macOS 26.7.1），仅安装 Command Line Tools（Swift 6.3.3，macOS SDK，无 Xcode / xcodebuild）。

## 结论：原生 Swift（AppKit + SwiftUI）菜单栏应用

| 维度 | Swift / SwiftUI | Tauri 2 | Electron |
|---|---|---|---|
| 开机自启 | `SMAppService.mainApp.register()`（macOS 13+，公开 API，系统设置里可见可关） | 需插件 `tauri-plugin-autostart`，底层仍是 LaunchAgent/`SMAppService` 的封装 | `app.setLoginItemSettings`，底层同上 |
| Touch Bar | `NSTouchBar` 公开 API，首响应者链即可；Control Strip 常驻需私有 `DFRFoundation`，不做 | 无官方支持，需自写 Rust/ObjC 桥 | 有 `TouchBar` API（封装 NSTouchBar），可用 |
| 「灵动岛」(刘海) | Mac 没有真灵动岛；做法是 `NSPanel`（borderless、`.statusBar` 层级、所有 Space）贴在屏顶中央，用 `NSScreen.safeAreaInsets.top>0` 判刘海、`auxiliaryTopLeft/RightArea` 算刘海宽度，黑底与刘海融为一体，悬停下拉展开。原生最顺手 | 可以做（窗口 API 够用），但刘海几何需要自写原生代码 | 同上，且透明无边框窗口在多屏/全屏下常见小毛病 |
| 内存 / 电量 | 常驻 ~25–40 MB，无 WebView 常驻进程 | ~60–90 MB（WKWebView） | ~150–250 MB，多进程 |
| 构建难度 | 本机只有 CLT：`swift build` 可以直接编 AppKit/SwiftUI，再用脚本拼 `.app` 并 ad-hoc 签名。不需要 Xcode | 需 Rust 工具链 + Node；本机未装 | 需 Node + 下载 Electron 二进制（约 100MB） |

选 Swift 的理由：三项「硬件相关」需求（自启、Touch Bar、刘海）都只有原生公开 API 能直接做；最轻；本机现有工具链能从命令行完整构建与测试；前端若用 Web 技术反而要重新实现一遍网页里已有的动效，收益不大。

参考：
- SMAppService: https://developer.apple.com/documentation/servicemanagement/smappservice
- NSTouchBar / 只有 2016–2019 的 MacBook Pro 带 Touch Bar（本机 Mac17,3 没有）: https://developer.apple.com/documentation/appkit/nstouchbar
- NSScreen.safeAreaInsets / auxiliaryTopLeftArea（刘海几何）: https://developer.apple.com/documentation/appkit/nsscreen/3882821-auxiliarytopleftarea
- ActivityKit（真正的灵动岛，仅 iPhone）: https://developer.apple.com/documentation/activitykit

## 真正的灵动岛（iPhone）

需要一个 iOS App + Widget Extension（ActivityKit Live Activity），只能在真机上看到灵动岛；装到真机需要 Apple 开发者账号（免费账号也可本机签名 7 天有效，但要 Xcode 和 iPhone）。本机没有 Xcode，无法构建 iOS 目标。作为后续项：复用本仓库 `HolaCore` 的 API 客户端与番茄钟逻辑（纯 Foundation，可直接共享到 iOS），再加 ~200 行 ActivityKit 代码即可。

## 登录与同步（读 server.py 得出）

- `POST /api/login` `{username,password}`，成功返回 `Set-Cookie: wc_session=<token>; HttpOnly; Max-Age=604800`（7 天）。服务端只存 sha256(token)。
- 所有 POST 必须带请求头 `X-Work-Calendar: 1`；若带 `Origin` 则要在白名单内——原生应用不发 Origin，直接通过。
- `GET /api/state` 带 `Cookie: wc_session=…` 返回全量快照（`user`、`today`（服务器 Asia/Shanghai 的今天）、`tasks`、`projects`…）。网页版「桌面小组件」的今日筛选逻辑：`date<=today<=deadline||date`、状态不在 deferred/skipped/rejected、未被 superseded、项目未 done/archived、非管理员只看自己的；按 P0/P1/P2 再按名称排序，未完成排前。
- 完成：`POST /api/action` `{action:'task.complete',data:{id,summary:'完成任务',actual:duration||30,feeling:'顺利',stackPeriods?}}`，与网页小组件完全一致；服务端会以 403/409 拒绝（非本人任务、管理员账号、已记录过），应用把 `error` 文本原样显示并把勾恢复。
- 轮询：空闲 45 s 一次；打开弹窗时若距上次同步 >10 s 立刻刷新；`NSWorkspace.willSleepNotification` 停表、`didWakeNotification` 立即同步并恢复。
- 离线：`URLError` 归为 offline，保留上次任务列表并显示「离线 · 上次同步 HH:mm」，不弹错。401 视为会话失效 → 清 Keychain 中的 token 并回到登录页。
- 存储：token 只进 Keychain（`kSecClassGenericPassword`，service `cn.hola.desktop.session`，account=服务器地址），URLSession 用 `ephemeral` 配置并关闭 Cookie 自动存取，所以 token 不会落到 `~/Library/Cookies`。服务器地址、用户名、样式偏好、番茄钟状态存在 UserDefaults（非敏感）。

## 服务端建议（本次未改 server.py）

`/api/state` 是全量快照（任务、日志、报告、日记、会议…），桌面端每 45 s 拉一次对团队库而言偏重。建议加一个轻量只读接口，例如 `GET /api/today`，只返回 `{user, today, tasks(今天活跃、属于本人), projects(id,name,status)}` 并支持 `If-None-Match`/`ETag` 返回 304。客户端已按「拿到快照就筛选」的方式写，接口一旦存在只需把路径换掉。
