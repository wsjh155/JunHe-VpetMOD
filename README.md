# VPet-Plugin-DayTrip · 一天特种兵旅行

给 [VPet 桌宠模拟器](https://github.com/LorisYounger/VPet) 增加「出门旅行」玩法的插件。花费金币派遣桌宠出门旅行，到点后带回**风景照 + 旅行感想**，并随机获得经验与属性奖励。

- 插件显示名：`特种兵旅行`
- 作者：君赫
- 框架：.NET 8（WPF）
- 游戏版本要求：≥ 11070（`gamever#11070`）

## 功能

| 旅行方式 | 时长 | 费用 | 体力 |
|---|---|---|---|
| 🌿 踏青远行 | 2 小时 | 5,000 | -40% |
| 🐎 策马疾行 | 2 小时 | 20,000 | -40% |
| ☁️ 闲云野鹤 | 8 小时 | 5,000 | +40% |
| 🎐 风雅云游 | 8 小时 | 20,000 | +40% |

- 完成后：经验 +10,000 ~ 100,000，并随机加成 2 项属性（好感度 / 饱腹度 / 口渴度 / 心情）。
- 旅行记录持久化到 `mod/trip_history.lps`，重启游戏自动续算未完成的行程。
- 风景照与文案数据来自 `mod/image/feelings.lps`，图片在 `mod/image/`。

## 目录结构

```
VPet-Plugin-DayTrip/
├── src/                         # 反编译源码（可编译工程）
│   ├── VPet.Plugin.DayTrip.csproj
│   ├── winDayTripMain.baml      # 编译版 XAML（见下方说明）
│   ├── winDayTripSetting.baml
│   ├── Properties/AssemblyInfo.cs
│   └── VPet.Plugin.DayTrip/
│       ├── DayTripPlugin.cs     # 插件主逻辑（MainPlugin）
│       ├── DayTripSetting.cs
│       ├── TripData.cs / TripMode.cs / TripPhoto.cs / TripRecord.cs
│       ├── winDayTripMain.cs
│       └── winDayTripSetting.cs
└── mod/                         # 原始 Mod 运行资源
    ├── info.lps
    ├── icon.png
    ├── trip_history.lps
    └── image/ …                 # 风景照 / feelings.lps
```

## 编译

1. 安装 .NET 8 SDK。
2. 把游戏安装目录下的以下 DLL 加入工程引用（属性 `HintPath` 指向 VPet 根目录）：
   - `VPet-Simulator.Core.dll`
   - `VPet-Simulator.Windows.Interface.dll`
   - `VPet-Simulator.Windows.dll`（如需）
   - `LinePutScript.dll`
   - `LinePutScript.Localization.WPF.dll`
3. `dotnet build -c Release`，产物 `VPet.Plugin.DayTrip.dll` 拷进游戏的 `mod/一天特种兵旅行/plugin/`。

> **关于 XAML**：WPF 窗口的界面定义被编译进了 `*.baml`。如需可编辑的 `*.xaml`，用 **ILSpy GUI** 或 **dnSpy** 打开 `VPet.Plugin.DayTrip.dll`，在资源树里找到对应 `BAML` 节点，右键“Decompile/XAML”导出即可。

## 安装（玩家）

把整个 `mod/` 目录（连同 `plugin/` 里的 `VPet.Plugin.DayTrip.dll`）放进
`<VPet安装目录>/mod/一天特种兵旅行/`，游戏内 **DIY → 特种兵旅行** 打开。

## 许可

MIT，见 [LICENSE](LICENSE)。

## 免责声明

非官方插件，与 VPet 作者无关。VPet Simulator 版权归其原作者所有。
