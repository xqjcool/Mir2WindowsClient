using System.Text;
using System.Text.Json;
using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MirWindowsClient;

internal sealed class WorldView : Control
{
    internal sealed record ItemView(int Id, string Name, int Mode, int Looks, int Luck, int Ac, int Mac, int Dc, int Mc, int Sc, int Durability, int MaxDurability);
    internal sealed record CharacterStats(string Ac, string Mac, string Dc, string Mc, string Sc);
    private sealed class MapData
    {
        public string Name { get; set; } = "地图";
        public int Width { get; set; }
        public int Height { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public bool[] Walkable { get; set; } = [];
        public List<GroundData> Ground { get; set; } = [];
        public List<ObjectData> Objects { get; set; } = [];
        public Dictionary<string, TextureData> Textures { get; set; } = [];
        public Dictionary<string, List<List<FrameData>>> Hero { get; set; } = [];
        public Dictionary<string, List<List<FrameData>>> Monster { get; set; } = [];
    }
    private sealed class GroundData { public string File { get; set; } = ""; public int X { get; set; } public int Y { get; set; } public int Width { get; set; } public int Height { get; set; } }
    private sealed class TextureData { public string File { get; set; } = ""; public int Width { get; set; } public int Height { get; set; } }
    private sealed class ObjectData { public string Texture { get; set; } = ""; public int X { get; set; } public int Y { get; set; } public int FrameCount { get; set; } = 1; public double FrameDuration { get; set; } = 0.1; public string? Blend { get; set; } public int? OffsetX { get; set; } public int? OffsetY { get; set; } }
    private sealed class FrameData { public string File { get; set; } = ""; public int Width { get; set; } public int Height { get; set; } public int OffsetX { get; set; } public int OffsetY { get; set; } }
    private sealed class Actor
    {
        public uint Id; public int X, Y, Direction; public int Race, Appearance; public string Name = "角色"; public bool IsPlayer;
        public double MovedAt; public int FromX, FromY; public bool Dead, Harvested; public string Action = "stand"; public double ActionUntil;
    }
    private sealed record GroundDrop(uint Id, int X, int Y, string Name, ushort Image);

    private readonly string _assetRoot;
    private Bitmap? _sceneSurface;
    private Bitmap? _lightSurface;
    private Image? _hudBoardImage;
    public Action<Graphics>? HudBoardPaint { get; set; }
    private readonly Dictionary<string, Bitmap> _images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, Actor> _actors = [];
    private readonly Dictionary<uint, GroundDrop> _drops = [];
    private readonly Dictionary<string, Dictionary<string, List<List<FrameData>>>> _actorLibraries = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private MapData? _map;
    private uint _playerId;
    private int _cameraX, _cameraY;
    private List<(int X, int Y)> _route = [];
    private int _routeIndex;
    private uint? _targetActor;
    private uint? _pendingNpcId;
    private DateTime _nextWalk = DateTime.MinValue;
    private DateTime _nextAttack = DateTime.MinValue;
    private bool _routeRunning;
    private int _lastRunDirection;
    private MouseButtons _heldMoveButton;
    private Point _heldMovePoint;
    private DateTime _nextHeldMove = DateTime.MinValue;
    private bool _mouseDownOnMap;
    private readonly List<ItemView> _bagItems = [];
    private readonly Dictionary<int, ItemView> _equipment = [];
    private readonly List<string> _skills = [];
    private int _level;
    private CharacterStats _stats = new("--", "--", "--", "--", "--");
    private int _hp, _maxHp, _mp, _maxMp;
    private string _combatStats = "等级 --   攻击 --   魔法 --   道术 --   防御 --   魔防 --";
    private string _status = "等待世界数据";

    public string MapName => _map?.Name ?? "地图";
    public string PositionText => _actors.TryGetValue(_playerId, out var p) ? $"{MapName}  {p.X},{p.Y}" : "等待位置";
    public IReadOnlyList<ItemView> BagItems => _bagItems;
    public IReadOnlyDictionary<int, ItemView> Equipment => _equipment;
    public IReadOnlyList<string> Skills => _skills;
    public int Level => _level;
    public int CurrentHp => _hp;
    public int MaximumHp => _maxHp;
    public int CurrentMp => _mp;
    public int MaximumMp => _maxMp;
    public CharacterStats Stats => _stats;
    public string VitalText => $"HP {_hp}/{_maxHp}    MP {_mp}/{_maxMp}";
    public string HpText => $"HP {_hp}/{_maxHp}";
    public string MpText => $"MP {_mp}/{_maxMp}";
    public string CombatStats => _combatStats;
    public string PlayerName => _actors.TryGetValue(_playerId, out var p) ? p.Name : "角色";
    public (uint Actor, ushort X, ushort Y)? UserQueryTarget => _actors.TryGetValue(_playerId, out var p)
        ? (_playerId, checked((ushort)(p.X + (_map?.OriginX ?? 0))), checked((ushort)(p.Y + (_map?.OriginY ?? 0)))) : null;

    public WorldView(string assetRoot)
    {
        _assetRoot = assetRoot;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            var libraryPath = Path.Combine(assetRoot, "actor-libraries.json");
            var library = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<List<FrameData>>>>>(File.ReadAllText(libraryPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (library != null) foreach (var pair in library) _actorLibraries[pair.Key] = pair.Value;
        }
        catch { }
        DoubleBuffered = true;
        BackColor = Color.FromArgb(17, 24, 19);
        _timer.Tick += (_, _) => { AdvanceRoute(); ContinueHeldMovement(); UpdateCamera(); Invalidate(); };
        _timer.Start();
        LoadMap("0");
    }

    public event Action<int, int, int>? WalkRequested;
    public event Action<int, int, int>? RunRequested;
    public event Action<int, int, int>? AttackRequested;
    public event Action<int, int>? PickupRequested;
    public event Action<string>? StatusChanged;
    public event Action? CharacterDataChanged;
    public event Action<string>? WorldActionDiagnostic;
    public event Action<string>? WorldChatReceived;
    public event Action<uint>? NpcTalkRequested;
    public event Action<uint, string>? NpcDialogChanged;
    public event Action? NpcDialogClosed;

    public void SetHudBoard(Image? image) { _hudBoardImage = image; Invalidate(); }

    public void PickupAtPlayer()
    {
        if (_actors.TryGetValue(_playerId, out var player))
        {
            var x = player.X + (_map?.OriginX ?? 0); var y = player.Y + (_map?.OriginY ?? 0);
            WorldActionDiagnostic?.Invoke($"pickup-button player={_playerId} at=({x},{y})");
            SetStatus("正在请求拾取角色周围物品");
            PickupRequested?.Invoke(x, y);
        }
        else SetStatus("尚未收到角色位置，暂时不能拾取");
    }

    public void Accept(ServerPacket p)
    {
        if (p.Id == 52 && p.Body.Length == 40)
        {
            _level = p.Body[0]; _hp = ReadWord(p.Body, 12); _mp = ReadWord(p.Body, 14); _maxHp = ReadWord(p.Body, 16); _maxMp = ReadWord(p.Body, 18);
            _stats = new CharacterStats(Range(p.Body, 2), Range(p.Body, 4), Range(p.Body, 6), Range(p.Body, 8), Range(p.Body, 10));
            _combatStats = $"等级 {_level}   攻击 {Range(p.Body, 6)}   魔法 {Range(p.Body, 8)}   道术 {Range(p.Body, 10)}   防御 {Range(p.Body, 2)}   魔防 {Range(p.Body, 4)}";
            CharacterDataChanged?.Invoke(); Invalidate(); return;
        }
        if (p.Id is 31 or 53)
        {
            if (p.Id == 31) { _hp = p.X; _maxHp = p.Y; }
            else { _hp = p.X; _mp = p.Y; _maxHp = p.Series; }
            CharacterDataChanged?.Invoke();
        }
        if (p.Id == 201)
        {
            _bagItems.Clear(); _bagItems.AddRange(p.BodyParts.Select(ParseItem).Where(x => x != null).Cast<ItemView>());
            CharacterDataChanged?.Invoke(); return;
        }
        if (p.Id is 200 or 203)
        {
            var item = ParseItem(p.Body); if (item != null) { _bagItems.RemoveAll(x => x.Name == item.Name); _bagItems.Add(item); CharacterDataChanged?.Invoke(); }
            return;
        }
        if (p.Id == 202)
        {
            var item = ParseItem(p.Body); if (item != null) _bagItems.RemoveAll(x => x.Name == item.Name);
            CharacterDataChanged?.Invoke(); return;
        }
        if (p.Id == 621)
        {
            _equipment.Clear();
            for (var i = 0; i + 1 < p.BodyParts.Count; i += 2)
                if (int.TryParse(Encoding.ASCII.GetString(p.BodyParts[i]), out var slot) && ParseItem(p.BodyParts[i + 1]) is { } item) _equipment[slot] = item;
            CharacterDataChanged?.Invoke(); return;
        }
        if (p.Id == 751 && p.Body.Length == 1688)
        {
            _equipment.Clear();
            for (var slot = 0; slot < 13; slot++)
            {
                var record = p.Body.AsSpan(60 + slot * 124, 124).ToArray();
                if (ParseItem(record) is { } item) _equipment[slot] = item;
            }
            CharacterDataChanged?.Invoke(); Invalidate(); return;
        }
        if (p.Id == 211)
        {
            _skills.Clear();
            foreach (var record in p.BodyParts)
                if (record.Length >= 12 && record[10] is > 0 and <= 14 && record.Length >= 11 + record[10])
                    _skills.Add(Encoding.GetEncoding("GB18030").GetString(record, 11, record[10]));
            CharacterDataChanged?.Invoke(); return;
        }
        if (p.Id is 100 or 101 or 102 or 103 or 104 or 1501)
        {
            var message = Encoding.GetEncoding("GB18030").GetString(p.Body).Trim('\0', ' ', '\r', '\n');
            if (!string.IsNullOrWhiteSpace(message))
            {
                WorldChatReceived?.Invoke(message);
                if (p.Id == 100) SetStatus(message);
            }
            return;
        }
        if (p.Id == 643)
        {
            var text = Encoding.GetEncoding("GB18030").GetString(p.Body).Trim('\0', ' ', '\r', '\n')
                .Split('/', 2, StringSplitOptions.None).Last().Replace("\\", "\r\n");
            NpcDialogChanged?.Invoke(p.Actor, text);
            return;
        }
        if (p.Id == 644) { NpcDialogClosed?.Invoke(); return; }
        if (p.Id == 610)
        {
            var x = (int)p.X - (_map?.OriginX ?? 0); var y = (int)p.Y - (_map?.OriginY ?? 0);
            if (_map != null && x >= 0 && y >= 0 && x < _map.Width && y < _map.Height)
            {
                var name = Encoding.GetEncoding("GB18030").GetString(p.Body).Trim('\0', ' ', '*');
                _drops[p.Actor] = new GroundDrop(p.Actor, x, y, string.IsNullOrWhiteSpace(name) ? "地面物品" : name, p.Series);
                SetStatus($"地面物品：{name}；点拾取周围物品按钮收集");
            }
            Invalidate(); return;
        }
        if (p.Id == 611) { _drops.Remove(p.Actor); Invalidate(); return; }
        if (p.Id == 51 || p.Id == 634)
        {
            var mapId = Encoding.ASCII.GetString(p.Body).Trim('\0', ' ', '\r', '\n');
            if (!string.IsNullOrWhiteSpace(mapId)) LoadMap(mapId);
        }
        if (p.Id is 30 or 32 or 34 or 33)
        {
            if (p.Id == 30) _actors.Remove(p.Actor);
            else if (_actors.TryGetValue(p.Actor, out var dead)) { dead.Dead = true; dead.Harvested = p.Id == 33; dead.Action = dead.Harvested ? "skeleton" : "die"; dead.ActionUntil = Environment.TickCount64 / 1000.0 + 1.2; }
            Invalidate(); return;
        }
        if (p.Id == 31 && p.Actor == _playerId && p.Body.Length >= 12 && _targetActor == null)
        {
            var attackerId = BinaryPrimitives.ReadUInt32LittleEndian(p.Body.AsSpan(8, 4));
            if (_actors.TryGetValue(attackerId, out var attacker) && attacker.Race is >= 10 and < 50 && !attacker.Dead)
                Attack(attacker);
        }
        if (p.Id is 8 or 14 or 15 or 16 or 18 or 19 or 24 && _actors.TryGetValue(p.Actor, out var acting))
        { acting.Action = "attack"; acting.ActionUntil = Environment.TickCount64 / 1000.0 + 0.6; acting.Direction = p.Series & 7; }
        if (p.Id is not (50 or 10 or 11 or 9 or 13 or 27 or 28)) return;
        var worldX = (int)p.X; var worldY = (int)p.Y;
        if (_map != null) { worldX -= _map.OriginX; worldY -= _map.OriginY; }
        if (p.Id == 50) _playerId = p.Actor;
        if (!_actors.TryGetValue(p.Actor, out var actor))
        {
            actor = new Actor { Id = p.Actor, X = worldX, Y = worldY, FromX = worldX, FromY = worldY };
            _actors[p.Actor] = actor;
        }
        actor.FromX = actor.X; actor.FromY = actor.Y; actor.X = worldX; actor.Y = worldY;
        actor.Direction = p.Series & 7; actor.MovedAt = Environment.TickCount64 / 1000.0;
        if (p.Id == 11) { actor.Action = "walk"; actor.ActionUntil = actor.MovedAt + 0.54; }
        else if (p.Id == 13) { actor.Action = "run"; actor.ActionUntil = actor.MovedAt + 0.38; }
        else if (p.Id == 9) { actor.Action = "walk"; actor.ActionUntil = actor.MovedAt + 0.54; }
        else if (p.Id is 10 or 27 or 50) { actor.Action = "stand"; actor.ActionUntil = 0; actor.Dead = false; }
        actor.IsPlayer = p.Actor == _playerId;
        if (actor.IsPlayer && p.Id is 9 or 11 or 13 or 28 or 50) _route.Clear();
        if (actor.IsPlayer && p.Id is 9 or 11 or 13) _routeRunning = false;
        if (actor.IsPlayer && p.Id is 9 or 11 or 13)
        {
            WorldActionDiagnostic?.Invoke($"server-move-ack packet={p.Id} actor={p.Actor} at=({worldX + (_map?.OriginX ?? 0)},{worldY + (_map?.OriginY ?? 0)})");
            SetStatus("服务器已确认移动");
        }
        if (p.Body.Length >= 8)
        {
            actor.Race = p.Body[0]; actor.Appearance = p.Body[2] | (p.Body[3] << 8);
            actor.Name = actor.IsPlayer ? "我" : actor.Race == 50 ? "NPC" : actor.Race switch { 11 when actor.Appearance == 160 => "鸡", 11 when actor.Appearance == 161 => "鹿", 18 when actor.Appearance == 27 => "稻草人", 17 when actor.Appearance == 25 => "多钩猫", _ => "角色" };
            if (p.Body.Length > 8 && !actor.IsPlayer)
            {
                var name = Encoding.GetEncoding("GB18030").GetString(p.Body, 8, p.Body.Length - 8).Split('/')[0].Trim('\0', '*', ' ');
                if (!string.IsNullOrWhiteSpace(name) && name.Length < 32) actor.Name = name;
            }
        }
        if (p.Id == 28)
        {
            actor.X = worldX; actor.Y = worldY; _route.Clear(); _routeIndex = 0;
            if (actor.IsPlayer && _routeRunning)
            {
                _routeRunning = false;
                var (dx, dy) = _lastRunDirection switch { 0 => (0,-1), 1 => (1,-1), 2 => (1,0), 3 => (1,1), 4 => (0,1), 5 => (-1,1), 6 => (-1,0), 7 => (-1,-1), _ => (0,0) };
                if (CanWalk(worldX + dx, worldY + dy))
                {
                    _route = [(worldX + dx, worldY + dy)];
                    SetStatus("\u524d\u65b9\u53d7\u963b\uff0c\u6539\u4e3a\u6b65\u884c");
                    AdvanceRoute();
                }
                else SetStatus("\u8dd1\u6b65\u53d7\u963b\uff0c\u4f4d\u7f6e\u5df2\u6821\u6b63");
            }
            else SetStatus("\u670d\u52a1\u5668\u62d2\u7edd\u4e86\u79fb\u52a8\uff0c\u4f4d\u7f6e\u5df2\u6821\u6b63");
        }
        if (actor.IsPlayer) { _cameraX = actor.X * 48 + 24; _cameraY = actor.Y * 32 + 16; }
        Invalidate();
    }

    private static int ReadWord(byte[] b, int at) => b[at] | b[at + 1] << 8;
    private static string Range(byte[] b, int at) => $"{ReadWord(b, at) & 255}-{ReadWord(b, at) >> 8}";
    private static ItemView? ParseItem(byte[] b)
    {
        if (b.Length != 124 || b[0] is 0 or > 14) return null;
        var name = Encoding.GetEncoding("GB18030").GetString(b, 1, b[0]);
        return new ItemView(BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(100, 4)), name, b[15], ReadWord(b, 22), b[111], ReadWord(b, 26), ReadWord(b, 28), ReadWord(b, 30), ReadWord(b, 32), ReadWord(b, 34), ReadWord(b, 104), ReadWord(b, 106));
    }

    private void LoadMap(string id)
    {
        var file = id == "0" ? Path.Combine(_assetRoot, "scene.json") : Path.Combine(_assetRoot, "maps", id, "scene.json");
        try
        {
            var json = File.ReadAllText(file);
            var map = JsonSerializer.Deserialize<MapData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("地图文件格式无效");
            if (map.Width is < 1 or > 1024 || map.Height is < 1 or > 1024 || map.Walkable.Length != map.Width * map.Height)
                throw new InvalidDataException("地图尺寸或行走数据无效");
            _map = map; _actors.Clear(); _drops.Clear(); _route.Clear(); _routeIndex = 0;
            _cameraX = map.Width * 24; _cameraY = map.Height * 16;
            SetStatus($"已加载 {map.Name}；点击地图移动，点拾取按钮收集附近物品");
        }
        catch (Exception ex) { SetStatus($"地图加载失败：{ex.Message}"); }
        Invalidate();
    }

    private void SetStatus(string text) { _status = text; StatusChanged?.Invoke(text); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_map == null) { e.Graphics.Clear(BackColor); DrawLabel(e.Graphics, _status, 18, 18, Color.OrangeRed); return; }
        EnsureRenderSurfaces();
        using var g = Graphics.FromImage(_sceneSurface!);
        g.Clear(BackColor); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var left = _cameraX - Width / 2; var top = _cameraY - Height / 2;
        var view = new Rectangle(0, 0, Width, Height);
        foreach (var chunk in _map.Ground)
        {
            var dest = new Rectangle(chunk.X - left, chunk.Y - top, chunk.Width, chunk.Height);
            if (!dest.IntersectsWith(view)) continue;
            var image = ImageFor(chunk.File);
            if (image != null) g.DrawImage(image, dest);
        }
        var lights = new List<(Bitmap Image, Rectangle Source, Rectangle Destination)>();
        foreach (var obj in _map.Objects.OrderBy(o => o.Y).ThenBy(o => o.X))
        {
            if (!_map.Textures.TryGetValue(obj.Texture, out var texture)) continue;
            var frameCount = Math.Max(1, obj.FrameCount); var h = texture.Height / frameCount;
            var isLight = string.Equals(obj.Blend, "add", StringComparison.OrdinalIgnoreCase);
            var x = obj.X * 48 + (isLight ? obj.OffsetX ?? 0 : 0);
            var y = isLight ? obj.Y * 32 + (obj.OffsetY ?? 0) : (obj.Y + 1) * 32 - h;
            var dest = new Rectangle(x - left, y - top, texture.Width, h);
            if (!dest.IntersectsWith(view)) continue;
            var image = ImageFor(texture.File); if (image == null) continue;
            var frame = frameCount > 1 ? (int)(Environment.TickCount64 / Math.Max(50, obj.FrameDuration * 1000)) % frameCount : 0;
            var source = new Rectangle(0, frame * h, texture.Width, h);
            if (isLight) lights.Add((image, source, dest));
            else g.DrawImage(image, dest, source, GraphicsUnit.Pixel);
        }
        foreach (var drop in _drops.Values)
        {
            var x = drop.X * 48 + 24 - left; var y = drop.Y * 32 + 20 - top;
            using var coin = new SolidBrush(Color.FromArgb(230, 255, 217, 71));
            g.FillEllipse(coin, x - 7, y - 7, 14, 14);
            using var font = new Font("Microsoft YaHei UI", 8, FontStyle.Bold);
            var size = g.MeasureString(drop.Name, font);
            using var label = new SolidBrush(Color.FromArgb(235, 0, 0, 0));
            g.FillRectangle(label, x - size.Width / 2 - 2, y + 7, size.Width + 4, size.Height);
            g.DrawString(drop.Name, font, Brushes.White, x - size.Width / 2, y + 7);
        }
        foreach (var actor in _actors.Values.OrderBy(a => a.Y)) DrawActor(g, actor, left, top);
        g.Flush();
        g.Dispose();
        if (lights.Count > 0) ApplyAdditiveLights(lights);
        using var hudGraphics = Graphics.FromImage(_sceneSurface!);
        // Paint the HUD in the same surface as the map. WinForms' transparent
        // PictureBox only reveals its parent background, which created a black
        // strip wherever the original HUD bitmap is transparent.
        var hudX = Math.Max(0, (Width - 800) / 2);
        var hudY = Math.Max(0, Height - 600) + 349;
        if (_hudBoardImage != null) hudGraphics.DrawImage(_hudBoardImage, new Rectangle(hudX, hudY, 800, 251));
        var state = hudGraphics.Save();
        hudGraphics.TranslateTransform(hudX, hudY);
        HudBoardPaint?.Invoke(hudGraphics);
        hudGraphics.Restore(state);
        e.Graphics.DrawImageUnscaled(_sceneSurface!, 0, 0);
        // The classic client has no permanent debug/status banner over the map.
        // Status messages are routed to the original chat panel by the host form.
    }

    private void DrawActor(Graphics g, Actor actor, int left, int top)
    {
        var now = Environment.TickCount64 / 1000.0;
        var duration = actor.ActionUntil - actor.MovedAt;
        var progress = actor.Action is "walk" or "run" && duration > 0
            ? Math.Clamp((now - actor.MovedAt) / duration, 0, 1) : 1;
        var drawX = actor.FromX + (actor.X - actor.FromX) * progress;
        var drawY = actor.FromY + (actor.Y - actor.FromY) * progress;
        var frame = CurrentFrame(actor);
        if (frame != null)
        {
            var x = (int)Math.Round(drawX * 48 + frame.OffsetX); var y = (int)Math.Round(drawY * 32 + 16 - frame.OffsetY - frame.Height);
            var dest = new Rectangle(x - left, y - top, frame.Width, frame.Height);
            if (dest.IntersectsWith(new Rectangle(0, 0, Width, Height)))
            {
                var image = ImageFor(frame.File); if (image != null) g.DrawImage(image, dest);
            }
        }
        var centerX = (int)Math.Round(drawX * 48 + 24) - left; var footY = (int)Math.Round(drawY * 32 + 30) - top;
        using var brush = new SolidBrush(actor.IsPlayer ? Color.Gold : Color.White);
        using var font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold);
        var size = g.MeasureString(actor.Name, font);
        g.DrawString(actor.Name, font, brush, centerX - size.Width / 2, footY - 72);
    }

    private FrameData? CurrentFrame(Actor actor)
    {
        var key = actor.IsPlayer ? $"human-{actor.Appearance >> 8}" : actor.Race == 0 ? "human-0" : $"{actor.Race}-{actor.Appearance}";
        _actorLibraries.TryGetValue(key, out var library);
        if (library == null) _actorLibraries.TryGetValue(actor.Race == 0 ? "human-0" : "18-27", out library);
        if (library == null) library = actor.Race == 0 ? _map!.Hero : _map!.Monster;
        FrameData? frame = null;
        var now = Environment.TickCount64 / 1000.0;
        var selectedAction = actor.Dead ? (actor.Harvested ? "skeleton" : "die") : actor.ActionUntil > now ? actor.Action : "stand";
        if (library != null && library.TryGetValue(selectedAction, out var dirs) && dirs.Count > actor.Direction && dirs[actor.Direction].Count > 0)
            frame = dirs[actor.Direction][(int)(Environment.TickCount64 / 240 % dirs[actor.Direction].Count)];
        else if (library != null && library.TryGetValue("stand", out dirs) && dirs.Count > actor.Direction && dirs[actor.Direction].Count > 0)
            frame = dirs[actor.Direction][(int)(Environment.TickCount64 / 240 % dirs[actor.Direction].Count)];
        return frame;
    }

    private Bitmap? ImageFor(string relative)
    {
        if (_images.TryGetValue(relative, out var cached)) return cached;
        try
        {
            var full = Path.Combine(_assetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            using var stream = File.OpenRead(full); using var decoded = new Bitmap(stream); var bitmap = new Bitmap(decoded);
            _images[relative] = bitmap; return bitmap;
        }
        catch { _images[relative] = null!; return null; }
    }

    private static void DrawLabel(Graphics g, string text, int x, int y, Color color)
    {
        using var brush = new SolidBrush(color); using var font = new Font("Microsoft YaHei UI", 9, FontStyle.Bold); g.DrawString(text, font, brush, x, y);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_mouseDownOnMap) { _mouseDownOnMap = false; return; }
        HandleMapClick(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Right && !IsHudPoint(e.Location))
        {
            _heldMoveButton = e.Button;
            _heldMovePoint = e.Location;
            _mouseDownOnMap = true;
            Focus();
            HandleMapClick(e);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_heldMoveButton != MouseButtons.None) _heldMovePoint = e.Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == _heldMoveButton) _heldMoveButton = MouseButtons.None;
    }

    private bool IsHudPoint(Point point)
    {
        const int classicWidth = 800;
        const int hudHeight = 251;
        return new Rectangle(Math.Max(0, (Width - classicWidth) / 2), Math.Max(0, Height - hudHeight), classicWidth, hudHeight).Contains(point);
    }

    private void HandleMapClick(MouseEventArgs e)
    {
        if (_map == null) return;
        // The original HUD remains an 800x251 panel centered at the bottom
        // when the window is maximized. Do not treat the old y=349 screen
        // coordinate as HUD everywhere; that made most of a maximized map
        // ignore left clicks.
        if (IsHudPoint(e.Location)) return;
        if (e.Button is not (MouseButtons.Left or MouseButtons.Right)) return;
        var x = (e.X + _cameraX - Width / 2) / 48; var y = (e.Y + _cameraY - Height / 2) / 32;
        x = Math.Clamp(x, 0, _map.Width - 1); y = Math.Clamp(y, 0, _map.Height - 1);
        if (e.Button == MouseButtons.Right)
        {
            _targetActor = null;
            BeginGroundMove((x, y), run: true);
            return;
        }
        if ((ModifierKeys & Keys.Shift) != 0 && _actors.TryGetValue(_playerId, out var self))
        {
            var direction = Direction(self.X, self.Y, x, y, self.Direction);
            AttackAt(self.X + _map.OriginX, self.Y + _map.OriginY, direction, "Shift+左键原地攻击");
            return;
        }
        var start = _actors.TryGetValue(_playerId, out var player) ? (X: player.X, Y: player.Y) : (X: x, Y: y);
        WorldActionDiagnostic?.Invoke($"left-click screen=({e.X},{e.Y}) map=({x + _map.OriginX},{y + _map.OriginY}) player={_playerId} known={_actors.ContainsKey(_playerId)} start=({start.X},{start.Y})");
        var cameraLeft = _cameraX - Width / 2; var cameraTop = _cameraY - Height / 2;
        var clickedActor = _actors.Values.Where(a => a.Id != _playerId && !a.Dead && (a.IsPlayer || a.Race is >= 10 and <= 50))
            .OrderByDescending(a => a.Y)
            .FirstOrDefault(a =>
            {
                var frame = CurrentFrame(a);
                var bounds = frame == null
                    ? new Rectangle(a.X * 48 - cameraLeft, a.Y * 32 - 48 - cameraTop, 48, 80)
                    : new Rectangle(a.X * 48 + frame.OffsetX - cameraLeft, a.Y * 32 + 16 - frame.OffsetY - frame.Height - cameraTop, frame.Width, frame.Height);
                return bounds.Contains(e.Location);
            })
            ?? _actors.Values.Where(a => a.Id != _playerId && !a.Dead && (a.IsPlayer || a.Race is >= 10 and <= 50) && Math.Abs(a.X - x) <= 1 && y >= a.Y - 1 && y <= a.Y + 1)
                .OrderBy(a => Math.Abs(a.X - x) + Math.Abs(a.Y - y)).FirstOrDefault();
        if (clickedActor is { Race: 50 })
        {
            _targetActor = null;
            _pendingNpcId = clickedActor.Id;
            if (Math.Max(Math.Abs(clickedActor.X - start.X), Math.Abs(clickedActor.Y - start.Y)) <= 3)
            {
                _pendingNpcId = null;
                NpcTalkRequested?.Invoke(clickedActor.Id);
                return;
            }
            var approach = Enumerable.Range(0, 8).Select(i => (X: clickedActor.X + new[] { -1, 0, 1, -1, 1, -1, 0, 1 }[i], Y: clickedActor.Y + new[] { -1, -1, -1, 0, 0, 1, 1, 1 }[i]))
                .Where(c => CanWalk(c.X, c.Y)).Select(c => (Cell: c, Path: FindPath(start, c))).Where(c => c.Path.Count > 0).OrderBy(c => c.Path.Count).FirstOrDefault();
            _route = approach.Path ?? []; _routeIndex = 0;
            if (_route.Count == 0) { _pendingNpcId = null; SetStatus($"无法接近 {clickedActor.Name}"); return; }
            SetStatus($"正在接近 {clickedActor.Name}"); AdvanceRoute();
            return;
        }
        if (clickedActor != null && (clickedActor.IsPlayer || clickedActor.Race is >= 10 and < 50))
        {
            _routeRunning = false;
            _targetActor = clickedActor.Id;
            if (Math.Max(Math.Abs(clickedActor.X - start.X), Math.Abs(clickedActor.Y - start.Y)) <= 1) Attack(clickedActor);
            else
            {
                var approach = Enumerable.Range(0, 8).Select(i => (X: clickedActor.X + new[] { -1, 0, 1, -1, 1, -1, 0, 1 }[i], Y: clickedActor.Y + new[] { -1, -1, -1, 0, 0, 1, 1, 1 }[i]))
                    .Where(c => CanWalk(c.X, c.Y)).Select(c => (Cell: c, Path: FindPath(start, c))).Where(c => c.Path.Count > 0).OrderBy(c => c.Path.Count).FirstOrDefault();
                _route = approach.Path ?? []; _routeIndex = 0;
                if (_route.Count == 0) { _targetActor = null; SetStatus("无法接近这个目标"); return; }
                SetStatus($"正在接近 {clickedActor.Name}"); AdvanceRoute();
            }
            return;
        }
        _targetActor = null;
        _pendingNpcId = null;
        BeginGroundMove((x, y), run: false);
    }

    private void BeginGroundMove((int X, int Y) destination, bool run)
    {
        if (!_actors.TryGetValue(_playerId, out var player) || _map == null)
        { SetStatus("等待服务器同步角色位置"); return; }
        var path = FindPath((player.X, player.Y), destination);
        if (path.Count == 0) { SetStatus("目标不可达，或已在该位置"); return; }
        _routeRunning = false;
        _pendingNpcId = null;
        if (run && path.Count >= 2)
        {
            var first = path[0]; var second = path[1];
            if (second.X - first.X == first.X - player.X && second.Y - first.Y == first.Y - player.Y)
            { _route = [second]; _routeRunning = true; }
            else _route = [first];
        }
        else _route = [path[0]];
        _routeIndex = 0;
        SetStatus(run ? "正在跑向点击位置" : "正在走向点击位置");
        AdvanceRoute();
    }

    private void ContinueHeldMovement()
    {
        if (_heldMoveButton == MouseButtons.None || DateTime.UtcNow < _nextHeldMove || _routeIndex < _route.Count || _targetActor != null || _pendingNpcId != null) return;
        if (IsHudPoint(_heldMovePoint) || _map == null || !_actors.TryGetValue(_playerId, out var player)) return;
        var x = (_heldMovePoint.X + _cameraX - Width / 2) / 48;
        var y = (_heldMovePoint.Y + _cameraY - Height / 2) / 32;
        x = Math.Clamp(x, 0, _map.Width - 1); y = Math.Clamp(y, 0, _map.Height - 1);
        if (x == player.X && y == player.Y) return;
        _nextHeldMove = DateTime.UtcNow.AddMilliseconds(180);
        BeginGroundMove((x, y), run: _heldMoveButton == MouseButtons.Right);
    }

    private List<(int X, int Y)> FindPath((int X, int Y) start, (int X, int Y) goal)
    {
        if (_map == null || !CanWalk(goal.X, goal.Y) || start == goal) return [];
        var count = _map.Width * _map.Height; var source = start.Y * _map.Width + start.X; var target = goal.Y * _map.Width + goal.X;
        var parent = Enumerable.Repeat(-1, count).ToArray(); var distance = Enumerable.Repeat(int.MaxValue, count).ToArray(); var closed = new bool[count];
        var queue = new PriorityQueue<int, int>(); distance[source] = 0; queue.Enqueue(source, Math.Max(Math.Abs(start.X-goal.X), Math.Abs(start.Y-goal.Y)));
        while (queue.TryDequeue(out var current, out _))
        {
            if (closed[current]) continue; if (current == target) break; closed[current] = true;
            var cx = current % _map.Width; var cy = current / _map.Width;
            for (var dy=-1;dy<=1;dy++) for(var dx=-1;dx<=1;dx++)
            {
                if (dx==0 && dy==0) continue; var nx=cx+dx;var ny=cy+dy;if(!CanWalk(nx,ny))continue;
                if(dx!=0&&dy!=0&&(!CanWalk(cx+dx,cy)||!CanWalk(cx,cy+dy)))continue;
                var next=ny*_map.Width+nx; if(closed[next]||distance[current]+1>=distance[next])continue;
                distance[next]=distance[current]+1;parent[next]=current;
                queue.Enqueue(next,distance[next]+Math.Max(Math.Abs(nx-goal.X),Math.Abs(ny-goal.Y)));
            }
        }
        if (parent[target] < 0) return [];
        var path = new List<(int X,int Y)>(); for(var at=target;at!=source;at=parent[at])path.Add((at%_map.Width,at/_map.Width));path.Reverse();return path;
    }
    private bool CanWalk(int x,int y) => _map != null && x>=0 && y>=0 && x<_map.Width && y<_map.Height && _map.Walkable[y*_map.Width+x];
    private void UpdateCamera()
    {
        if (!_actors.TryGetValue(_playerId, out var player)) return;
        var now = Environment.TickCount64 / 1000.0;
        var duration = player.ActionUntil - player.MovedAt;
        var progress = player.Action is "walk" or "run" && duration > 0
            ? Math.Clamp((now - player.MovedAt) / duration, 0, 1) : 1;
        _cameraX = (int)Math.Round((player.FromX + (player.X - player.FromX) * progress) * 48 + 24);
        _cameraY = (int)Math.Round((player.FromY + (player.Y - player.FromY) * progress) * 32 + 16);
    }

    private void EnsureRenderSurfaces()
    {
        if (_sceneSurface?.Width == Width && _sceneSurface.Height == Height && _lightSurface?.Width == Width && _lightSurface.Height == Height) return;
        _sceneSurface?.Dispose(); _lightSurface?.Dispose();
        var width = Math.Max(1, Width); var height = Math.Max(1, Height);
        _sceneSurface = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _lightSurface = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    }

    private void ApplyAdditiveLights(List<(Bitmap Image, Rectangle Source, Rectangle Destination)> lights)
    {
        using (var g = Graphics.FromImage(_lightSurface!))
        {
            g.Clear(Color.Transparent); g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            foreach (var light in lights) g.DrawImage(light.Image, light.Destination, light.Source, GraphicsUnit.Pixel);
        }
        var rect = new Rectangle(0, 0, _sceneSurface!.Width, _sceneSurface.Height);
        var sceneData = _sceneSurface.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var lightData = _lightSurface!.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var length = Math.Abs(sceneData.Stride) * sceneData.Height;
            var scene = new byte[length]; var light = new byte[length];
            Marshal.Copy(sceneData.Scan0, scene, 0, length); Marshal.Copy(lightData.Scan0, light, 0, length);
            for (var y = 0; y < sceneData.Height; y++)
            {
                var row = y * sceneData.Stride;
                for (var x = 0; x < sceneData.Width; x++)
                {
                    var i = row + x * 4; var alpha = light[i + 3];
                    if (alpha == 0) continue;
                    scene[i] = (byte)Math.Min(255, scene[i] + light[i] * alpha / 255);
                    scene[i + 1] = (byte)Math.Min(255, scene[i + 1] + light[i + 1] * alpha / 255);
                    scene[i + 2] = (byte)Math.Min(255, scene[i + 2] + light[i + 2] * alpha / 255);
                }
            }
            Marshal.Copy(scene, 0, sceneData.Scan0, length);
        }
        finally { _sceneSurface.UnlockBits(sceneData); _lightSurface.UnlockBits(lightData); }
    }
    private void AdvanceRoute()
    {
        if (_routeIndex >= _route.Count)
        {
            if (_pendingNpcId is uint npcId && _actors.TryGetValue(npcId, out var npc) && _actors.TryGetValue(_playerId, out var npcPlayer) && Math.Max(Math.Abs(npcPlayer.X - npc.X), Math.Abs(npcPlayer.Y - npc.Y)) <= 3)
            {
                _pendingNpcId = null;
                NpcTalkRequested?.Invoke(npcId);
            }
            if (_targetActor is uint targetId && _actors.TryGetValue(targetId, out var target) && !target.Dead && _actors.TryGetValue(_playerId, out var me) && Math.Max(Math.Abs(me.X-target.X),Math.Abs(me.Y-target.Y)) <= 1) Attack(target);
            return;
        }
        if (DateTime.UtcNow < _nextWalk) return;
        if (!_actors.TryGetValue(_playerId, out var player))
        {
            SetStatus("等待服务器同步角色位置，尚未发送移动请求");
            WorldActionDiagnostic?.Invoke($"move-not-sent player={_playerId} actor-count={_actors.Count}");
            return;
        }
        var next = _route[_routeIndex++]; var fromX = player.X; var fromY = player.Y; var dx=next.X-player.X;var dy=next.Y-player.Y;
        var dir = Direction(player.X, player.Y, next.X, next.Y, player.Direction);
        var targetX = next.X + _map!.OriginX; var targetY = next.Y + _map.OriginY;
        var now = Environment.TickCount64 / 1000.0;
        player.FromX = player.X; player.FromY = player.Y;
        player.X = next.X; player.Y = next.Y; player.Direction = dir; player.MovedAt = now;
        player.Action = _routeRunning ? "run" : "walk";
        player.ActionUntil = now + (_routeRunning ? 0.55 : 0.4);
        _cameraX = player.X * 48 + 24; _cameraY = player.Y * 32 + 16;
        _nextWalk = DateTime.UtcNow.AddMilliseconds(600);
        WorldActionDiagnostic?.Invoke($"move-send player={_playerId} from=({fromX + _map.OriginX},{fromY + _map.OriginY}) to=({targetX},{targetY}) direction={dir} running={_routeRunning}");
        SetStatus($"移动请求已发送：{targetX},{targetY}");
        if (_routeRunning) { _lastRunDirection = dir; RunRequested?.Invoke(targetX,targetY,dir); }
        else WalkRequested?.Invoke(targetX,targetY,dir);
        Invalidate();
    }
    private static int Direction(int fromX, int fromY, int toX, int toY, int fallback) =>
        (Math.Sign(toX - fromX), Math.Sign(toY - fromY)) switch
        { (0,-1)=>0,(1,-1)=>1,(1,0)=>2,(1,1)=>3,(0,1)=>4,(-1,1)=>5,(-1,0)=>6,(-1,-1)=>7,_=>fallback };

    private void AttackAt(int x, int y, int direction, string description)
    {
        if (DateTime.UtcNow < _nextAttack) return;
        AttackRequested?.Invoke(x, y, direction);
        _nextAttack = DateTime.UtcNow.AddMilliseconds(900);
        SetStatus($"\u6b63\u5728\u653b\u51fb\uff1a{description}");
    }

    private void Attack(Actor target)
    {
        if (DateTime.UtcNow < _nextAttack) return;
        if (!_actors.TryGetValue(_playerId, out var player)) return;
        var dx=target.X-player.X;var dy=target.Y-player.Y;
        var direction=(dx,dy) switch { (0,-1)=>0,(1,-1)=>1,(1,0)=>2,(1,1)=>3,(0,1)=>4,(-1,1)=>5,(-1,0)=>6,(-1,-1)=>7,_=>player.Direction };
        AttackRequested?.Invoke(player.X + _map!.OriginX, player.Y + _map.OriginY, direction);
        player.Action = "attack";
        player.ActionUntil = Environment.TickCount64 / 1000.0 + 0.6;
        player.Direction = direction;
        _nextAttack = DateTime.UtcNow.AddMilliseconds(900);
        SetStatus($"正在攻击 {target.Name}；伤害由服务器判定");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); foreach(var image in _images.Values) image?.Dispose(); _images.Clear(); }
        base.Dispose(disposing);
    }
}
