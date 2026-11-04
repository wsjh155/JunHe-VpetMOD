# VPet-Plugin.QQBody · QQBody桥接

把 [VPet 桌宠模拟器](https://github.com/LorisYounger/VPet) 变成 QQ 机器人的「实体身体」：
机器人说话，桌宠替她说；机器人思考，桌宠切「直播」动画干活；机器人烧 token，桌宠身上一颗颗光团飞走。

- 插件显示名：`QQBody桥接`
- 作者：君赫
- 框架：.NET 8（WPF）
- 游戏版本要求：≥ 11075（`gamever#11075`）
- 通信方式：**本机 HTTP**（默认 `127.0.0.1:52816`），不联外网、不需要注册、不需要任何账号

## 一句话简介

> 机器人的嘴和手，桌宠的身体：她说的话桌宠替她说，她思考时桌宠在忙着，她花钱时桌宠看得见。

## 为什么用 HTTP 而不是绑定某个框架

插件本体只起一个**纯 HTTP 服务**，不含任何平台 SDK。QQ、微信、飞书、Discord、Telegram……
只要你的机器人框架能发 HTTP 请求，就能驱动这个 mod；换个框架不用重编译，改几行调用就行。

AstrBot / NoneBot / 自建脚本 / 甚至一行 `curl` 都能用，详见仓库内的《接口文档.md》。

## 功能

| 能力 | 接口 | 说明 |
| --- | --- | --- |
| 机器人说话 | `POST /say` | 桌宠把这条回复「说」出来 |
| 切工作动画 | `POST /action` | `stream` / `live` / `直播` → 切直播动画；`normal` → 回待机 |
| 彩色气泡 | `POST /bubble` | 蓝 = 缓存命中、红 = 未命中、白 = 总结，向上淡出 |
| token 光团 | `POST /fly` | 一小团金色光球从桌宠身上飞走淡出（约 2 秒，球上不带数字） |
| 累计计数 | `POST /token`、`POST /token/reset` | 右上角常驻徽标，≤4 字符自动缩写（`1.5k` / `1.2m`） |
| 回传桌宠的话 | `GET /events?since=N` | 轮询取桌宠自己说的话，转发回 QQ |
| 聊天框接机器人 | TalkAPI（`DIY` 类型） | 在桌宠聊天框里打字，直接和机器人对话 |
| 功能开关 | `GET /config` | 5 个开关，存在 `config.json`，设置页可点 |

### 建议的调用节奏

```text
机器人开始生成   POST /action {name:"stream"}       # 切直播动画
生成中           POST /fly                          # 0.5s 后第一个，之后约 2s 一个
                 POST /token {delta:N}              # 拿到真实用量后累加，刷新右上角
生成结束         POST /action {name:"normal"}       # 回待机
                 POST /bubble {text:"命中 X", color:"blue"}
                 POST /bubble {text:"未命中 Y", color:"red"}
机器人发出回复   POST /say {text:"回复内容"}         # 桌宠替她说
```

## 安装

1. 下载本仓库，或自己编译。
2. 把 `info.lps`、`icon.png`、`plugin/` **整个目录**放进：

   ```text
   <你的 VPet 目录>/mod/1145142_QQBody/
   ```

   > 是 `mod`（不是 `mods`），目录名随意，但建议保持 `1145142_QQBody` 以免和其他 mod 混淆。

3. 重启 VPet。mod 列表出现「QQBody桥接」即安装成功。
4. 验证服务是否在跑：

   ```bash
   curl http://127.0.0.1:52816/ping
   ```

   返回 `{"ok":true,...}` 就说明接口通了。

## 编译

```bash
dotnet build -c Release -p:VPetGamePath="你的 VPet 安装目录"
```

- 需要 .NET 8 SDK。
- `VPetGamePath` 用来引用游戏目录里的 `VPet-Simulator.Windows.Interface.dll`、`VPet-Simulator.Core.dll`、`LinePutScript*.dll`、`Panuon.WPF*.dll`（都设了 `Private=false`，不会被打进输出）。
- 产物：`bin/Release/VPet.Plugin.QQBody.dll`，拷进 `mod/1145142_QQBody/plugin/` 即可。

## 功能开关

MOD 配置页 → `QQBody桥接` → **设置**，可以单独关掉：

| 开关 | 作用 |
| --- | --- |
| 思考时播工作动画 | 机器人干活时切直播动画 |
| 飞 token 光团 | 生成过程中的光球动画 |
| 右上角累计 token 计数 | 常驻数字徽标（另有「重置为 0」） |
| 弹命中 / 未命中气泡 | 结束时的彩色气泡 |
| 注册聊天框接口 | 关掉后桌宠聊天框的下拉列表里不再出现本接口（下次启动生效） |

开关存在 `mod/1145142_QQBody/config.json`，接口侧也能用 `GET /config` 先读一遍，省掉无用的请求。

## 文档

- **《接口文档.md》** —— 全部 HTTP 接口、字段、返回、伪代码，接任何框架都看这个
- **《使用方案.md》** —— 整体对接方案与落地方案

## 已知限制

- 只在本机通信（`127.0.0.1`），**没有鉴权**。想跨机器用请自己套一层反代或 SSH 隧道，别直接暴露到公网。
- 端口 `52816` 被占用时会在日志里报错，改端口需要改源码里 `HttpBridge` 的监听地址。
- 桌宠说话的时机取决于调用方；本 mod 不主动采集聊天内容，也不上传任何数据。

## 免责声明

本插件为个人开发，与 VPet 官方无关。使用中出现的存档异常、游戏崩溃等问题请自负风险；
如需反馈，请附上 `mod/1145142_QQBody/plugin/qqbody.log` 与复现步骤。

## License

[MIT](./LICENSE) © 2026 Junhe
