# Mir 1.76 Windows 客户端（C#）

这是 Delphi 客户端的独立重写起点，当前已实现 LoginGate、SelGate、GameGate 登录流程，以及基础联网场景。

## 构建与启动

在客户端仓库目录运行：

```powershell
D:\OpenMir2Tools\dotnet\dotnet.exe build MirWindowsClient.csproj -c Release
```

运行 `bin\Release\net8.0-windows\MirWindowsClient.exe`。登录、选角和窗口素材位于仓库 `Assets` 中。完整地图和动作数据没有复制进本仓库；在其他位置运行时，设置 `MIR2_GAMEDATA` 指向含 `scene.json` 的完整 `GameData` 目录。未设置时，程序会从客户端目录向上查找。目标电脑需要 .NET 8 Windows Desktop Runtime。

默认连接 `192.168.1.81` 的 7000、7100、7200 端口，服务器名为 `MirTest`。可在登录窗口修改服务器地址。

## 当前功能

- 账号登录/注册/改密、已有角色列表、创建/删除角色和进入游戏认证。
- 比奇省地图底图与场景物件，按服务端地图切换消息载入已导出的地图。
- 人物和怪物基础帧、服务器权威位置更新、点击地图寻路移动。
- 点击怪物发起攻击；没有手动目标时，收到怪物伤害会按服务端回传的攻击者位置反击。
- 显示服务端通知的地面物品；只有点击“拾取周围物品”按钮才会请求拾取，不会自动拾取。右键仍用于原版跑步操作。

完整的装备纸娃娃、所有战斗动作与技能、背包和 NPC 交互界面仍需移植；此阶段不应当作完整替代客户端。
