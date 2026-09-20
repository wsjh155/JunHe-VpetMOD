# VPet-Plugin-BackpackTrade · 交易系统（跳蚤超市）

给 [VPet 桌宠模拟器](https://github.com/LorisYounger/VPet) 增加**背包交易 / 跳蚤超市**与**优惠券**系统，并附带一批高收益自定义食物、工作与学习。

- 插件显示名：`跳蚤超市`（主插件 `VPet.Plugin.BackpackTrade`）
- 作者：君赫
- 框架：.NET 8（WPF）
- 游戏版本要求：≥ 11071（`gamever#11071`）
- 含两个插件程序集：`VPet.Plugin.BackpackTrade.dll`（交易系统）+ `VPet.Plugin.YouHuiKa.dll`（优惠券）

## 功能

### 跳蚤超市（`BackpackTrade`）
在 **吃饭菜单 → 跳蚤超市** 下新增三个菜单项：

- **交易系统**：打开 `TradeWindow`，买卖背包中的可交易物品。
- **优惠券查询**：显示优惠券有效期（优先从 YouHuiKa 插件读取，回退到存档键 `YouHuiKa.CouponPeriod`）。
- **注销优惠券**：按剩余天数退还金币（退款 = `1,000,000 × 剩余天数/30`，四舍五入到分）。

交易规则：
- 买卖均收 **1% 税**（`TaxRate`）。
- 特价商品：`每日礼包` 买入 1,000 / 卖出 15；`锦囊` 买入 100 / 卖出 1。
- 可交易类型：`FoodType` 0/2/3/4/5/6/7。
- 所有上架物品在 `CloneFood` 时对 `Exp/Strength/StrengthFood/StrengthDrink/Feeling/Health/Likability/Price` 做**安全钳制**，避免数值溢出导致游戏崩溃。
- `锦囊` 使用时会开出 3 个随机道具（价格需在 10 ~ 上限之间）。

### 优惠券（`YouHuiKa`）
- 使用物品 `优惠券` → 有效期延长 30 天（`CouponValidDuration`），存档键 `YouHuiKa.CouponPeriod`。
- 持有效优惠券期间，任何喂食物品按原价 **9 折**返还（`Price × 0.9`）金币。

### 自定义内容（数据文件）
- `food/drug.lps`：万能营养液、经验暴涨丸系列、体力结晶、饱食炸弹、猫薄荷、优惠券 等。
- `pet/vup.lps`：黑心老板（Work）、严厉老师（Study）两种高收益工作/学习。
- `lang/zh-Hans/zh-Hans.lps`：上述内容的本地化文案。
- `text/SchedulePackage.lps`：免费的工作/学习安排包。

## 目录结构

```
VPet-Plugin-BackpackTrade/
├── src/
│   ├── VPet.Plugin.BackpackTrade.csproj
│   ├── VPet.Plugin.YouHuiKa.csproj
│   ├── Properties/AssemblyInfo.cs
│   ├── VPet.Plugin.BackpackTrade/
│   │   ├── BackpackTrade.cs      # 主插件：买卖/税/锦囊逻辑
│   │   └── TradeWindow.cs        # 交易窗口 UI
│   └── VPet.Plugin.YouHuiKa/
│       └── YouHuiKa.cs           # 优惠券插件
└── mod/
    ├── info.lps  ·  icon.png
    ├── food/drug.lps
    ├── pet/vup.lps
    ├── lang/zh-Hans/zh-Hans.lps
    ├── text/SchedulePackage.lps
    ├── image/food/…            # 食物图标
    └── plugin/VPet.Plugin.YouHuiKa.deps.json
```

## 编译

本工程含**两个**独立插件项目，分别编译：

1. 安装 .NET 8 SDK。
2. 引用游戏根目录下的 `VPet-Simulator.Core.dll`、`VPet-Simulator.Windows.Interface.dll`（设置 `HintPath`）。
3. `dotnet build src/VPet.Plugin.BackpackTrade.csproj -c Release`
   `dotnet build src/VPet.Plugin.YouHuiKa.csproj -c Release`
4. 两个 DLL 一起放进 `mod/交易系统/plugin/`。

> **关于 XAML**：`TradeWindow` 的界面被编译进 DLL 的 `BAML` 资源，用 **ILSpy GUI** / **dnSpy** 打开 DLL 可导出为 `*.xaml`。

## 许可

MIT，见 [LICENSE](LICENSE)。

## 免责声明

非官方插件，与 VPet 作者无关。VPet Simulator 版权归其原作者所有。
