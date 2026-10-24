# Delphi 客户端到 C# 的移植对照

本文将 `D:\Mir2ClientBuild-D6\MirClient` 中的原版 Delphi 客户端当作行为和协议依据。C# 客户端是独立重写，不能通过改几个窗体就视为移植完成：Delphi 侧依赖 DirectX/DelphiX、WIL 图像解码和多组场景/角色单元，C# 侧需要逐层替换这些运行时。

## 已核对的原版入口

| 原版职责 | Delphi 源码 | 当前 C# 对应 | 状态 |
| --- | --- | --- | --- |
| 程序与游戏主循环 | `mir2.dpr`、`ClMain.pas` | `Program.cs`、`Form1.cs` | C# 启动/窗体及服务连接已存在；与原版界面仍有差距 |
| 登录、注册、改密、建角、删除、选角 | `IntroScn.pas`（`TLoginScene`、`TSelectChrScene`）、`FState.pas` | `LoginClient.cs`、`Form1.cs` | 主要页面使用原始登录/选角/注册/改密素材，协议操作已接通；尚待真实服务器逐项验证 |
| 鼠标输入与动作派发 | `ClMain.pas`：`DXDrawMouseDown`、`_DXDrawMouseDown`、`AttackTarget`、`ProcessActionMessages`、`DXDrawMouseUp` | `WorldView.cs`：`HandleMapClick`、`BeginGroundMove`、`AdvanceRoute` | 左键单步走、左键点目标攻击/接近、Shift 左键站桩攻击、右键跑步、按住连续移动已有实现；细节仍需逐项对齐 |
| 鼠标坐标转地图格 | `PlayScn.pas`：`CXYfromMouseXY` | `WorldView.cs`：`HandleMapClick` | 已有映射；原版以角色屏幕偏移和 `UNITX/UNITY` 做等距格换算，C# 当前是简化网格，需以原图实测校准 |
| 碰撞与跑步校验 | `PlayScn.pas`：`CanWalk`、`CanRun`、`CrashMan`；`MapUnit.pas`：可行走标记 | `WorldView.cs`：`FindPath`、`CanWalk` | C# 可用地图步行格寻路；动态角色阻挡、开门、特殊地形和原版两格跑的逐格校验仍不完整 |
| 角色/怪物动作更新 | `Actor.pas`：`UpdateMsg`、`CalcActorFrame`；`PlayScn.pas`：服务端动作派发 | `WorldView.cs`：`Accept`、`CurrentFrame`、`DrawActor` | 基础待机/走/跑/攻击/死亡帧接入；装备纸娃娃、动作时长、受击及职业动作还未达到原版 |
| 网络命令/服务器回包 | `ClMain.pas`：`SendSocket`、`SendLogin`、`SendPickup`、服务端消息分派；`Common\EDcode.pas` | `LoginClient.cs`、`WorldView.cs` | 登录/基础游戏包和移动、攻击、拾取已接入；完整命令表和每种回包结构还需继续逐包比对 |
| 原版地图、场景物件和 HUD | `MapUnit.pas`、`PlayScn.pas`、`DrawScrn.pas`、`DWinCtl.pas` | `WorldView.cs`、`Assets` | C# 绘制路径独立于 DirectDraw；原版全部图层、窗口控件、透明混合及 UI 操作尚未完整移植 |

## 本轮核对出的输入规则

- 原版 `ClMain.pas` 在左键地面点击后设为 `caWalk`；攻击目标时，在相邻格直接发攻击包，否则计算目标周边可站格并走近/跑近。
- 原版右键地面点击设为 `caRun`，距离不足两格时转成走路；不能跑时回退到步行。
- 原版 Shift+左键攻击目标时不移动；未点中目标时可按当前方向发出原地攻击。
- 原版发出的移动命令编号为 `CM_WALK=3011`、`CM_RUN=3013`。服务端回来的动作编号包括 `SM_WALK=11`、`SM_RUN=13`、`SM_MOVEFAIL=28`。`Actor.UpdateMsg` 会先清理本地未完成动作，再发送新的动作，避免旧动作覆盖新输入。
- 服务器不向发起者回广播普通移动动作，因此 C# 不能把“等自己的移动广播”当作移动成功条件。当前 C# 先本地显示步/跑动作，并用 `SM_MOVEFAIL` 校正被拒绝的位置。
- 原版确有配置驱动的自动拾取位（`g_boAutoPuckUpItem` 和物品筛选表）；本项目按用户针对当前客户端的明确要求关闭自动拾取，只有点 HUD 拾取按钮才发 `CM_PICKUP=1001`。普通移动回包不应触发拾取。

## 当前 C# 移动和拾取修正

- `SM_RUN` 现在使用跑步动作帧；`SM_WALK` 和后退动作仍使用走步帧。
- 移除“角色移动到地面物品格时自动请求拾取”的代码。拾取按钮继续调用 `PickupAtPlayer()`，向服务端请求角色周围物品。
- 自动路径搜索仍是 C# 的简化实现。原版不是一次把全路径交给服务器，而是按动作锁、速度和方向逐步派发；C# 目前每次发一条步/跑命令，等待自身预测节奏，服务器拒绝时纠正。后续要补原版动作锁/速度条件和每类地图的碰撞规则。

## 本轮选角页移植

- 创建角色弹窗使用原图 `prguse-073.png`，位置和输入控件参照 `FState.pas` 的 `DCreateChr` 与 `TSelectChrScene.MakeNewChar`；职业、性别、发型和角色名进入原版 `CM_NEWCHR=101` 数据格式。
- 创建成功后按原版流程发 `CM_QUERYCHR=100` 刷新角色列表；删除使用 `CM_DELCHR=102`，收到成功响应后同样刷新列表。删除前要求用户明确确认。
- 服务端返回的失败码会转换成角色重名、非法名称、角色数量上限等消息。创建/删除协议已按 OpenMir2 的 SelGate/DBSrv 处理路径核对，仍需连接运行中的服务器做实测。

## 登录窗口补齐

- 新账号和修改密码按钮分别打开原图 `prguse-063.png`、`prguse-050.png`，输入框位置按 `IntroScn.pas` 的坐标设置。
- 注册按原版 `CM_ADDNEWUSER=2002` 发送两个 EDCode 编码的固定长度 Pascal 字段块；第一块 148 字节，第二块按当前 OpenMir2 `UserEntryAdd` 的字段容量序列化为 101 字节。服务器返回“注册关闭/账号重名/成功”等状态后提示用户。
- 修改密码按 `CM_CHANGEPASSWORD=2003` 发送账号、原密码、新密码，并比对两次新密码。此实现已通过编译，尚未在运行中的 LoginSrv 做账户操作验证。

## 尚未完成的重制范围

1. 完整还原原版登录、创建/删除角色、选角、进入游戏和退出流程。
2. 解析并使用原版角色装备组合帧，恢复人物属性/装备/背包/技能窗口与交互。
3. 对照原版 `WIL`、地图层和 `DrawScrn` 重做地图、HUD、遮挡、透明和动画时序。
4. 按 `Grobal2.pas` 和服务端源码逐项校验命令号、字段宽度、字节序、字符串编码及回包布局。
5. 在真实服务端验证移动、拒绝校正、攻击、拾取、地图切换、重登和异常退出；构建成功只表示代码编译通过，不代表这些行为已通过实机验证。
