# VPet-Plugin-OnlineInteraction · VPet 联机互动

在 [VPet 桌宠模拟器](https://github.com/LorisYounger/VPet) 的**联机模式**中，为好友之间增加互动玩法：投喂、打招呼、发红包。

- 插件显示名：`VPet联机互动`
- 作者：君赫
- 框架：.NET 8（WPF，x64）
- 游戏版本要求：≥ 11070（`gamever#11070`）

## 功能

在联机窗口的 `TabControl` 中注入两个页签：**互动**、**互动日志**。

- **打招呼**：`[GREET] 消息`，可对单个好友或所有人发送，对方头顶弹出聊天气泡。
- **发红包**：赠送金币。单次上限 = `min(double.MaxValue, 5000 + 等级×100)`；群发时按好友数扣费。
- **批量投喂**：把背包食物投喂给好友（数量上限 99），对方按食物属性直接加成；若对方没有该食物定义，则回退到原始属性（`RawStats`）加成。
- **互动动作**：摸头 / 摸身子 / 捏脸（`Interact` 枚举 0/1/2）。
- 消息类型自定义：`100 = 红包(RedPacketData)`、`101 = 批量投喂(BatchFeedData)`。
- 收到的互动统一记录在「互动日志」面板。

## 目录结构

```
VPet-Plugin-OnlineInteraction/
├── src/
│   ├── VPet.Plugin.OnlineInteraction.csproj
│   ├── Properties/AssemblyInfo.cs
│   └── VPet.Plugin.OnlineInteraction/
│       ├── OnlineInteraction.cs   # 插件主逻辑（MainPlugin）
│       ├── InteractionPanel.cs    # 互动页签 UI
│       ├── LogPanel.cs            # 日志页签 UI
│       ├── RedPacketData.cs
│       └── BatchFeedData.cs
└── mod/
    ├── info.lps
    └── icon.png
```

## 编译

1. 安装 .NET 8 SDK。
2. 引用游戏根目录下的 `VPet-Simulator.Core.dll`、`VPet-Simulator.Windows.Interface.dll`、`LinePutScript.dll`（设置 `HintPath`）。
3. `dotnet build -c Release`，`VPet.Plugin.OnlineInteraction.dll` 拷进 `mod/<你的mod目录>/plugin/`。

## 许可

MIT，见 [LICENSE](LICENSE)。

## 免责声明

非官方插件，与 VPet 作者无关。联机协议相关类型（`IMainWindow.MutiPlayerHandle`、`MPMessage` 等）由 VPet 提供，版权归原作者。
