# VPet 桌宠 Mod · 开源源码包

对下列三个 VPet 桌宠 mod 的插件 DLL 做了反编译，整理成**可直接开源到 GitHub** 的仓库目录。

| 仓库目录 | Mod | 主插件 | 说明 |
|---|---|---|---|
| `VPet-Plugin-DayTrip/` | 一天特种兵旅行 | `VPet.Plugin.DayTrip` | 派遣桌宠出门旅行并带回风景照与奖励 |
| `VPet-Plugin-OnlineInteraction/` | VPet联机互动 | `VPet.Plugin.OnlineInteraction` | 联机好友间投喂 / 打招呼 / 发红包 |
| `VPet-Plugin-BackpackTrade/` | 交易系统（跳蚤超市） | `VPet.Plugin.BackpackTrade` + `VPet.Plugin.YouHuiKa` | 背包买卖、优惠券、自定义食物/工作 |

每个仓库目录的结构：

```
<仓库>/
├── README.md          # 项目说明
├── LICENSE            # MIT
├── .gitignore
├── .gitattributes     # 统一换行、标记二进制
├── src/               # 反编译得到的 C# 源码（含 .csproj）
└── mod/               # 原始 Mod 运行资源（info.lps / image / *.lps 数据）
```

> 发布步骤见 **[GITHUB_开源发布指南.md](GITHUB_开源发布指南.md)**。

## 反编译信息

- 工具：`ilspycmd 8.2.0`（ILSpy 命令行，`-p` 工程模式）
- 目标框架：`.NET 8` / `WPF`
- 原始 DLL：
  - `一天特种兵旅行/plugin/VPet.Plugin.DayTrip.dll`
  - `1145141_OnlineInteraction/plugin/VPet.Plugin.OnlineInteraction.dll`
  - `交易系统/plugin/VPet.Plugin.BackpackTrade.dll`、`VPet.Plugin.YouHuiKa.dll`

## 已知限制

1. **WPF 的 XAML 为编译版 `.baml`**：`ilspycmd` 只导出 `.baml`。要在工程里编辑界面，用 **ILSpy GUI** 或 **dnSpy** 打开 DLL，把资源树里的 BAML 节点导出为 `*.xaml`。
2. **工程引用**：反编译的 `.csproj` 只写了程序集名，未含 `HintPath`。编译前需把游戏目录下的 `VPet-Simulator.Core.dll`、`VPet-Simulator.Windows.Interface.dll`、（如需）`VPet-Simulator.Windows.dll`、`LinePutScript*.dll` 加为引用。
3. **反编译产物**含少量 IL 注释（如 `//IL_xxxx: Unknown result type`）与 `ref` 局部变量写法，属正常现象，不影响阅读。
4. `mod/` 中**不含编译好的 `.dll`**，也不含 `*.bak_*` 备份文件。发布二进制请在 GitHub Releases 里附加。

## 版权说明

- 作者：君赫（`authorid#1591629865`）
- 许可：MIT（可自行改为 Apache-2.0 / GPL 等）
- VPet 桌宠模拟器版权归其原作者所有，本项目为**非官方**插件。
