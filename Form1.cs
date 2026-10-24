using System.Text;

namespace MirWindowsClient;

public sealed class Form1 : Form
{
    private readonly TextBox _host = new() { Text = "192.168.1.81", Width = 220 };
    private readonly TextBox _account = new() { Width = 220 };
    private readonly TextBox _password = new() { Width = 220, UseSystemPasswordChar = true };
    private readonly TextBox _server = new() { Text = "MirTest", Width = 220 };
    private readonly Button _login = new() { Text = "登录", Width = 220, Height = 42 };
    private readonly Label _status = new() { AutoSize = true, Text = string.Empty };
    private readonly Panel _characters = new() { Bounds = new Rectangle(0, 0, 800, 600), Visible = false, BackColor = Color.Black };
    private readonly LoginClient _client = new();
    private readonly Panel _loginPanel = new() { Bounds = new Rectangle(0, 0, 800, 600), BackColor = Color.Black };
    private Label? _characterStatus;
    private readonly Label?[] _selectName = new Label?[2];
    private readonly Label?[] _selectLevel = new Label?[2];
    private readonly Label?[] _selectJob = new Label?[2];
    private readonly PictureBox?[] _selectPortrait = new PictureBox?[2];
    private WorldView? _world;
    private Label? _worldStatus;
    private CharacterInfo? _activeCharacter;
    private readonly List<Button> _bagSlots = [];
    private readonly Label[] _equipmentSlots = new Label[13];
    private readonly List<PictureBox> _paperdollImages = [];
    private readonly ToolTip _itemTips = new();
    private readonly Dictionary<int, Image?> _itemIcons = [];
    private readonly Dictionary<int, Image?> _uiFrameCache = [];
    private Label? _vitalsLabel;
    private Label? _statsLabel;
    private Label? _sidebarStatsLabel;
    private Label? _mpLabel;
    private readonly List<Label> _skillRows = [];
    private ListBox? _chatLog;
    private TextBox? _chatInput;
    private Panel? _viewportPanel;
    private PictureBox? _bottomBoard;
    private Panel? _characterWindow;
    private Panel? _bagWindow;
    private Panel? _skillWindow;
    private Label? _bagDetails;
    private WorldView.ItemView? _selectedBagItem;
    private bool _selfStateRequested;

    public Form1()
    {
        Text = "Mir 1.76";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ClientSize = new Size(800, 600);
        BackColor = Color.Black;
        ForeColor = Color.White;
        Font = new Font("Tahoma", 9f);

        _host.Visible = false;
        _server.Visible = false;
        BuildOriginalLoginScreen();
        BuildOriginalCharacterScreen();
        _login.Click += async (_, _) => await LoginAsync();
        Controls.Add(_loginPanel);
        Controls.Add(_characters);
        AcceptButton = _login;
        _client.WorldPacketReceived += packet =>
        {
            if (IsDisposed) return;
            if (packet.Id == 50 && !_selfStateRequested)
            {
                _selfStateRequested = true;
                _ = _client.QueryUserStateAsync(packet.Actor, packet.X, packet.Y);
            }
            if (packet.Id is 615 or 619 && _world?.UserQueryTarget is { } target)
            {
                _ = RefreshInventoryAsync(target.Actor, target.X, target.Y);
            }
            BeginInvoke(new Action(() => _world?.Accept(packet)));
        };
        _client.WorldConnectionEnded += ex =>
        {
            if (IsDisposed) return;
            BeginInvoke(new Action(() => { if (_world != null && _worldStatus != null) _worldStatus.Text = $"世界连接已断开：{ex.Message}"; }));
        };
        FormClosed += (_, _) => _client.Dispose();
    }

    private void BuildOriginalLoginScreen()
    {
        var background = LoadUiImage("login.png");
        if (background != null) _loginPanel.BackgroundImage = background;
        _loginPanel.BackgroundImageLayout = ImageLayout.None;

        // Classic 1.76 coordinates from IntroScn.TLoginScene.OpenScene and
        // FState.TFrmDlg.Initialize. The input controls remain editable, but
        // every surrounding pixel comes from the original client artwork.
        AddArt(_loginPanel, 60, 252, 173);
        _account.SetBounds(350, 258, 112, 16);
        StyleOriginalEdit(_account);
        _password.SetBounds(350, 290, 112, 16);
        StyleOriginalEdit(_password);
        _login.SetBounds(252 + 169, 173 + 163, 76, 32);
        StyleArtButton(_login, 62);
        _loginPanel.Controls.Add(_account);
        _loginPanel.Controls.Add(_password);
        _loginPanel.Controls.Add(_host);
        _loginPanel.Controls.Add(_server);
        _loginPanel.Controls.Add(_login);

        var create = MakeArtButton(61, 252 + 25, 173 + 207, 76, 32);
        create.Click += (_, _) => ShowLoginMessage("角色创建请使用已存在的游戏角色；当前服务端未启用注册。", 318, 330);
        var changePassword = MakeArtButton(53, 252 + 130, 173 + 207, 76, 32);
        changePassword.Click += (_, _) => ShowLoginMessage("当前服务端未启用客户端改密功能。", 318, 330);
        var close = MakeArtButton(64, 252 + 252, 173 + 28, 18, 20);
        close.Click += (_, _) => Close();
        _loginPanel.Controls.Add(create);
        _loginPanel.Controls.Add(changePassword);
        _loginPanel.Controls.Add(close);
        _status.SetBounds(318, 330, 250, 34);
        _status.AutoSize = false;
        _status.ForeColor = Color.White;
        _status.BackColor = Color.Transparent;
        _status.Font = new Font("Tahoma", 8.5f);
        _loginPanel.Controls.Add(_status);
        _login.BringToFront();
        _account.BringToFront();
        _password.BringToFront();
    }

    private void BuildOriginalCharacterScreen()
    {
        var background = LoadUiImage("characters.png");
        if (background != null) _characters.BackgroundImage = background;
        _characters.BackgroundImageLayout = ImageLayout.None;

        AddArt(_characters, 66, 133, 453);
        AddArt(_characters, 67, 685, 454);
        AddArt(_characters, 68, 385, 456);
        AddArt(_characters, 69, 348, 486);
        AddArt(_characters, 70, 347, 506);
        AddArt(_characters, 72, 379, 547);

        var firstSelect = MakeHotspot(133, 453, 76, 31);
        var secondSelect = MakeHotspot(685, 454, 76, 31);
        firstSelect.Click += (_, _) => SelectCharacterSlot(0);
        secondSelect.Click += (_, _) => SelectCharacterSlot(1);
        var start = MakeHotspot(385, 456, 76, 17);
        start.Click += async (_, _) => await StartSelectedCharacterAsync();
        var exit = MakeHotspot(379, 547, 76, 24);
        exit.Click += (_, _) => { _characters.Visible = false; _loginPanel.Visible = true; _account.Focus(); };
        _characters.Controls.Add(firstSelect);
        _characters.Controls.Add(secondSelect);
        _characters.Controls.Add(start);
        _characters.Controls.Add(exit);
        _characters.Tag = (new List<CharacterInfo>(), 0);
        var serverName = new Label { Bounds = new Rectangle(300, 5, 200, 18), Text = _server.Text, ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Tahoma", 8.25f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
        _characters.Controls.Add(serverName);
        for (var slot = 0; slot < 2; slot++)
        {
            var x = slot == 0 ? 117 : 671;
            _selectName[slot] = AddSelectText(x, slot == 0 ? 493 : 495, 110);
            _selectLevel[slot] = AddSelectText(x, slot == 0 ? 522 : 524, 48);
            _selectJob[slot] = AddSelectText(x, slot == 0 ? 552 : 554, 80);
        }
        _characterStatus = AddSelectText(308, 568, 300);
        _characterStatus.TextAlign = ContentAlignment.MiddleCenter;
    }

    private Label AddSelectText(int x, int y, int width)
    {
        var label = new Label { Bounds = new Rectangle(x, y, width, 20), ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Tahoma", 8.25f, FontStyle.Bold), AutoSize = false };
        _characters.Controls.Add(label);
        return label;
    }

    private void StyleOriginalEdit(TextBox input)
    {
        input.BorderStyle = BorderStyle.None;
        input.BackColor = Color.Black;
        input.ForeColor = Color.White;
        input.Font = new Font("Microsoft Sans Serif", 10f);
        input.MaxLength = input == _account ? 14 : 10;
    }

    private void StyleArtButton(Button button, int frame)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.Transparent;
        button.FlatAppearance.MouseOverBackColor = Color.Transparent;
        button.BackColor = Color.Transparent;
        button.Text = string.Empty;
        button.Image = LoadUiFrame(frame);
        button.TabStop = false;
    }

    private Button MakeArtButton(int frame, int x, int y, int width, int height)
    {
        var button = new Button { Bounds = new Rectangle(x, y, width, height) };
        StyleArtButton(button, frame);
        return button;
    }

    private Button MakeHotspot(int x, int y, int width, int height) => new()
    {
        Bounds = new Rectangle(x, y, width, height),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.Transparent,
        Text = string.Empty,
        TabStop = false,
        FlatAppearance = { BorderSize = 0, MouseDownBackColor = Color.Transparent, MouseOverBackColor = Color.Transparent }
    };

    private void AddArt(Control parent, int frame, int x, int y)
    {
        var image = LoadUiFrame(frame);
        if (image == null) return;
        parent.Controls.Add(new PictureBox { Bounds = new Rectangle(x, y, image.Width, image.Height), Image = image, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent, Enabled = false });
    }

    private Image? LoadUiFrame(int frame)
    {
        if (_uiFrameCache.TryGetValue(frame, out var cached)) return cached;
        cached = LoadUiImage(Path.Combine("frames", $"prguse-{frame:000}.png"));
        _uiFrameCache[frame] = cached;
        return cached;
    }

    private void PaintOriginalBottomBoard(object? sender, PaintEventArgs e)
    {
        if (_world == null) return;
        var graphics = e.Graphics;
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        if (_activeCharacter?.Job == 0 && _world.Level < 28)
        {
            DrawUiFrame(graphics, 5, new Rectangle(38, 90, 94, 92));
            var fill = LoadUiFrame(6);
            if (fill != null && _world.MaximumHp > 0)
            {
                var top = Math.Clamp((int)Math.Round(fill.Height * (1d - (double)_world.CurrentHp / _world.MaximumHp)), 0, fill.Height);
                if (top < fill.Height) graphics.DrawImage(fill, new Rectangle(38, 90 + top, fill.Width - 2, fill.Height - top), new Rectangle(0, top, fill.Width - 2, fill.Height - top), GraphicsUnit.Pixel);
            }
        }
        else if (_world.MaximumHp > 0 && _world.MaximumMp > 0)
        {
            var orb = LoadUiFrame(4);
            if (orb != null)
            {
                var half = orb.Width / 2;
                var hpTop = Math.Clamp((int)Math.Round(orb.Height * (1d - (double)_world.CurrentHp / _world.MaximumHp)), 0, orb.Height);
                var mpTop = Math.Clamp((int)Math.Round(orb.Height * (1d - (double)_world.CurrentMp / _world.MaximumMp)), 0, orb.Height);
                if (hpTop < orb.Height) graphics.DrawImage(orb, new Rectangle(40, 91 + hpTop, half - 1, orb.Height - hpTop), new Rectangle(0, hpTop, half - 1, orb.Height - hpTop), GraphicsUnit.Pixel);
                if (mpTop < orb.Height) graphics.DrawImage(orb, new Rectangle(40 + half + 1, 91 + mpTop, orb.Width - half - 1, orb.Height - mpTop), new Rectangle(half + 1, mpTop, orb.Width - half - 1, orb.Height - mpTop), GraphicsUnit.Pixel);
            }
        }

        if (_world.Level > 0)
        {
            var digits = _world.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (var i = 0; i < digits.Length; i++)
                if (int.TryParse(digits[i].ToString(), out var digit)) DrawUiFrame(graphics, 30 + digit, new Rectangle(660 + (i + 1) * 8, 147, 8, 13));
        }
    }

    private void DrawUiFrame(Graphics graphics, int frame, Rectangle destination)
    {
        var image = LoadUiFrame(frame);
        if (image != null) graphics.DrawImage(image, destination, new Rectangle(Point.Empty, image.Size), GraphicsUnit.Pixel);
    }

    private void ShowLoginMessage(string message, int x, int y)
    {
        _status.Text = message;
        _status.SetBounds(x, y, 300, 40);
    }

    private void SetWorldStatus(string message)
    {
        if (_worldStatus == null || IsDisposed) return;
        _worldStatus.Text = message;
        _worldStatus.Visible = true;
    }

    private void SelectCharacterSlot(int index)
    {
        if (_characters.Tag is not ValueTuple<List<CharacterInfo>, int> selection || index >= selection.Item1.Count) return;
        _characters.Tag = (selection.Item1, index);
        _characters.Invalidate();
    }

    private void RemoveSelectPortrait(int slot)
    {
        if (_selectPortrait[slot] is not { } portrait) return;
        _characters.Controls.Remove(portrait);
        portrait.Image?.Dispose();
        portrait.Dispose();
        _selectPortrait[slot] = null;
    }

    private void AddSelectPortrait(int slot, CharacterInfo character)
    {
        var sourceIndex = 40 + Math.Clamp(character.Job, 0, 2) * 40 + Math.Clamp(character.Sex, 0, 1) * 120;
        var image = LoadUiImage(Path.Combine("frames", $"chrsel-{sourceIndex:000}.png"));
        if (image == null) return;
        var (baseX, baseY) = (character.Job, character.Sex) switch
        {
            (0, 0) => (71, 52), (0, _) => (65, 55),
            (1, 0) => (77, 46), (1, _) => (171, 97),
            (2, 0) => (85, 63), _ => (164, 103)
        };
        var x = baseX + (slot == 0 ? 0 : 340);
        var y = baseY + (slot == 0 ? 0 : 2);
        var portrait = new PictureBox
        {
            Bounds = new Rectangle(x, y, image.Width, image.Height),
            Image = image,
            SizeMode = PictureBoxSizeMode.Normal,
            BackColor = Color.Transparent,
            Enabled = false
        };
        _selectPortrait[slot] = portrait;
        _characters.Controls.Add(portrait);
        portrait.SendToBack();
        foreach (var control in _characters.Controls.Cast<Control>().Where(c => c is Button or Label)) control.BringToFront();
    }

    private async Task StartSelectedCharacterAsync()
    {
        if (_characters.Tag is ValueTuple<List<CharacterInfo>, int> selection && selection.Item2 < selection.Item1.Count)
            await EnterWorldAsync(selection.Item1[selection.Item2]);
    }

    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_account.Text) || string.IsNullOrEmpty(_password.Text))
        {
            _status.Text = "请输入账号和密码。";
            return;
        }
        SetBusy(true, "正在连接登录服务器…");
        try
        {
            var characters = await _client.LoginAsync(_host.Text.Trim(), _account.Text.Trim(), _password.Text, _server.Text.Trim());
            _password.Clear();
            _characters.Tag = (characters.Take(2).ToList(), 0);
            for (var slot = 0; slot < 2; slot++)
            {
                var character = slot < characters.Count ? characters[slot] : null;
                _selectName[slot]!.Text = character?.Name ?? string.Empty;
                _selectLevel[slot]!.Text = character?.Level.ToString() ?? string.Empty;
                _selectJob[slot]!.Text = character?.JobName ?? string.Empty;
                RemoveSelectPortrait(slot);
                if (character != null) AddSelectPortrait(slot, character);
            }
            _characterStatus!.Text = characters.Count == 0 ? "当前账号没有可进入的角色" : string.Empty;
            _characters.Invalidate();

            _loginPanel.Visible = false;
            _characters.Visible = true;
            _characters.BringToFront();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _password.Clear();
        }
        finally { SetBusy(false); }
    }

    private async Task EnterWorldAsync(CharacterInfo character)
    {
        if (_characterStatus != null) _characterStatus.Text = $"正在进入 {character.Name}…";
        SetBusy(true, $"正在进入 {character.Name}…");
        try
        {
            _activeCharacter = character;
            _selfStateRequested = false;
            await _client.EnterWorldAsync(character.Name);
            BuildWorldInterface(FindGameDataRoot());
            await _client.StartWorldAsync();
        }
        catch (Exception ex)
        {
            if (_characterStatus != null) _characterStatus.Text = ex.Message;
        }
        finally { SetBusy(false); }
    }

    private void BuildWorldInterface(string assetRoot)
    {
        if (_world == null) _world = new WorldView(assetRoot);
        _world.Dock = DockStyle.Fill;
        _world.WalkRequested += async (x, y, direction) =>
        {
            try { await _client.WalkAsync(x, y, direction); }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => _status.Text = ex.Message)); }
        };
        _world.AttackRequested += async (x, y, direction) =>
        {
            try { await _client.AttackAsync(x, y, direction); }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => { if (_worldStatus != null) _worldStatus.Text = ex.Message; })); }
        };
        _world.PickupRequested += async (x, y) =>
        {
            try { await _client.PickupAsync(x, y); }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => _status.Text = ex.Message)); }
        };
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _viewportPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _viewportPanel.Controls.Add(_world);
        _bottomBoard = new PictureBox { Image = LoadUiFrame(1), Bounds = new Rectangle(0, 349, 800, 251), SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent, Enabled = false };
        _bottomBoard.Paint += PaintOriginalBottomBoard;
        _viewportPanel.Controls.Add(_bottomBoard);
        _bottomBoard.BringToFront();

        // These controls are children of the original DBottom window. Its top
        // edge is y=349 in the 800x600 layout, so DFM Top values are relative
        // to that panel rather than to the screen.
        const int boardTop = 349;
        var stateButton = MakeArtButton(8, 643, boardTop + 61, 24, 24);
        stateButton.Click += (_, _) => ToggleClassicWindow(_characterWindow);
        var bagButton = MakeArtButton(9, 682, boardTop + 41, 24, 22);
        bagButton.Click += (_, _) => ToggleClassicWindow(_bagWindow);
        var magicButton = MakeArtButton(10, 722, boardTop + 21, 24, 23);
        magicButton.Click += (_, _) => ToggleClassicWindow(_skillWindow);
        var optionButton = MakeArtButton(11, 764, boardTop + 11, 24, 23);
        optionButton.Click += (_, _) => ShowLoginMessage("设置窗口尚未移植。", 640, 90);
        foreach (var button in new[] { stateButton, bagButton, magicButton, optionButton })
        {
            _viewportPanel.Controls.Add(button);
            button.BringToFront();
        }

        var pickup = new Button { Text = "拾取", Width = 42, Height = 24, Left = 745, Top = 568, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 30, 25), ForeColor = Color.White, Font = new Font("Tahoma", 8f) };
        pickup.FlatAppearance.BorderColor = Color.FromArgb(142, 119, 83);
        pickup.Click += (_, _) => _world?.PickupAtPlayer();
        _viewportPanel.Controls.Add(pickup);
        pickup.BringToFront();

        var worldStatus = new Label { Left = 209, Top = 472, Width = 390, Height = 18, ForeColor = Color.FromArgb(35, 31, 26), BackColor = Color.Transparent, Font = new Font("Tahoma", 8f), Text = string.Empty, Visible = false };
        _worldStatus = worldStatus;
        _world.StatusChanged += text => { if (!IsDisposed) { worldStatus.Text = text; worldStatus.Visible = !string.IsNullOrWhiteSpace(text); } };
        _viewportPanel.Controls.Add(worldStatus);
        worldStatus.BringToFront();
        _characterWindow = BuildCharacterWindow();
        _bagWindow = BuildBagWindow();
        _skillWindow = BuildSkillWindow();
        foreach (var popup in new[] { _characterWindow, _bagWindow, _skillWindow })
        {
            popup.Visible = false; popup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            popup.Location = new Point(800 - popup.Width, 52);
            _viewportPanel.Controls.Add(popup);
        }
        _viewportPanel.SizeChanged += (_, _) => { if (_viewportPanel != null) RepositionClassicWindows(); };
        root.Controls.Add(_viewportPanel);
        Controls.Add(root); root.BringToFront();
        _world.CharacterDataChanged += RefreshCharacterPanels;
        RefreshCharacterPanels();
        _characters.Visible = false;
        _loginPanel.Visible = false;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ClientSize = new Size(800, 600);
    }

    private Panel BuildClassicSidebar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 26, 34), Padding = new Padding(6) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = panel.BackColor };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var name = new Label { Dock = DockStyle.Fill, Text = _activeCharacter is { } c ? $"{c.Name}  Lv.{c.Level}" : "角色", TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(238, 205, 132), Font = new Font(Font.FontFamily, 10f, FontStyle.Bold) };
        var portrait = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = LoadCharacterPortrait(), BackColor = Color.FromArgb(12, 17, 23) };
        _vitalsLabel = new Label { Dock = DockStyle.Fill, Text = "HP --/--", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(235, 103, 91) };
        _mpLabel = new Label { Dock = DockStyle.Fill, Text = "MP --/--", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(120, 174, 244) };
        _sidebarStatsLabel = new Label { Dock = DockStyle.Fill, Text = "攻击 --  魔法 --\n道术 --  防御 --  魔防 --", ForeColor = Color.FromArgb(226, 219, 194), TextAlign = ContentAlignment.MiddleLeft };
        layout.Controls.Add(name, 0, 0); layout.Controls.Add(portrait, 0, 1); layout.Controls.Add(_vitalsLabel, 0, 2); layout.Controls.Add(_mpLabel, 0, 3); layout.Controls.Add(_sidebarStatsLabel, 0, 4);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, BackColor = panel.BackColor, Margin = Padding.Empty };
        buttons.Controls.Add(ClassicButton("人物  F10", (_, _) => ToggleClassicWindow(_characterWindow)));
        buttons.Controls.Add(ClassicButton("背包  F9", (_, _) => ToggleClassicWindow(_bagWindow)));
        buttons.Controls.Add(ClassicButton("能力  F11", (_, _) => ToggleClassicWindow(_skillWindow)));
        layout.Controls.Add(buttons, 0, 5);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "快捷栏  F1–F8", ForeColor = Color.FromArgb(238, 205, 132), TextAlign = ContentAlignment.MiddleLeft }, 0, 6);
        var quickbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = panel.BackColor };
        for (var i = 0; i < 8; i++) quickbar.Controls.Add(new Button { Text = $"F{i + 1}", Dock = DockStyle.Fill, Margin = new Padding(2), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(31, 43, 59), ForeColor = Color.FromArgb(238, 219, 170) }, i % 4, i / 4);
        layout.Controls.Add(quickbar, 0, 7); panel.Controls.Add(layout); return panel;
    }

    private Button ClassicButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, Width = 101, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 43, 34), ForeColor = Color.FromArgb(238, 219, 170), Margin = new Padding(2) };
        button.FlatAppearance.BorderColor = Color.FromArgb(146, 112, 55); button.Click += handler; return button;
    }

    private Panel BuildBagWindow()
    {
        var panel = new Panel { Width = 336, Height = 270, BackColor = Color.Black, BackgroundImage = LoadUiFrame(3), BackgroundImageLayout = ImageLayout.None };
        for (var i = 0; i < 40; i++)
        {
            var slot = new Button { FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Color.White, Text = "", TabStop = false, Tag = null };
            slot.SetBounds(20 + i % 8 * 36, 13 + i / 8 * 32, 36, 32); slot.FlatAppearance.BorderSize = 0; slot.FlatAppearance.MouseOverBackColor = Color.Transparent;
            slot.Click += (_, _) => { if (slot.Tag is WorldView.ItemView item) SelectBagItem(item); };
            slot.MouseDoubleClick += async (_, _) => { if (slot.Tag is WorldView.ItemView item) await ActivateBagItemAsync(item); };
            _bagSlots.Add(slot); panel.Controls.Add(slot); slot.BringToFront();
        }
        var repair = MakeArtButton(64, 10, 10, 48, 22);
        repair.Click += (_, _) => SetWorldStatus("修理功能请与铁匠交互。");
        var gold = MakeArtButton(29, 10, 190, 36, 21);
        gold.Click += (_, _) => SetWorldStatus("金币存取功能尚未接入服务器。");
        var close = MakeArtButton(371, 309, 202, 14, 20);
        close.Click += (_, _) => { if (_bagWindow != null) _bagWindow.Visible = false; };
        panel.Controls.Add(repair); repair.BringToFront();
        panel.Controls.Add(gold); gold.BringToFront();
        panel.Controls.Add(close); close.BringToFront();
        var use = new Button { Bounds = new Rectangle(251, 180, 48, 24), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, Text = "", TabStop = false };
        use.FlatAppearance.BorderSize = 0; use.Click += async (_, _) => { if (_selectedBagItem != null) await UseSelectedBagItemAsync(_selectedBagItem); };
        panel.Controls.Add(use); use.BringToFront();
        _bagDetails = new Label { Left = 60, Top = 208, Width = 245, Height = 48, ForeColor = Color.FromArgb(238, 219, 170), BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 8f), AutoEllipsis = true };
        panel.Controls.Add(_bagDetails); _bagDetails.BringToFront();
        return panel;
    }

    private Panel BuildCharacterWindow()
    {
        var panel = new Panel { Width = 232, Height = 325, BackColor = Color.Black, BackgroundImage = LoadUiFrame(370), BackgroundImageLayout = ImageLayout.None };
        var bodyImage = LoadUiFrame(_activeCharacter?.Sex == 1 ? 377 : 376);
        if (bodyImage != null) panel.Controls.Add(new PictureBox { Image = bodyImage, SizeMode = PictureBoxSizeMode.Normal, Bounds = new Rectangle(38, 52, bodyImage.Width, bodyImage.Height), BackColor = Color.Transparent, Enabled = false });
        var equipmentLabels = new (int Slot, int X, int Y, int W, int H)[]
        {
            (0, 96, 122, 53, 112), (1, 47, 70, 47, 87), (2, 169, 127, 34, 30), (3, 169, 88, 34, 30), (4, 115, 85, 18, 18),
            (5, 43, 177, 34, 30), (6, 169, 177, 34, 30), (7, 43, 217, 34, 30), (8, 169, 217, 34, 30), (9, 25, 232, 34, 30),
            (10, 77, 232, 34, 30), (11, 128, 232, 34, 30), (12, 182, 232, 34, 30)
        };
        foreach (var placement in equipmentLabels)
        {
            var label = MakeSlotLabel(); label.Dock = DockStyle.None; label.SetBounds(placement.X, placement.Y, placement.W, placement.H); label.Font = new Font("Tahoma", 6.5f); label.BackColor = Color.Transparent; label.TextAlign = ContentAlignment.MiddleCenter;
            _equipmentSlots[placement.Slot] = label; panel.Controls.Add(label); label.BringToFront();
        }
        _statsLabel = new Label { Left = 114, Top = 98, Width = 112, Height = 150, ForeColor = Color.White, BackColor = Color.Transparent, Font = new Font("Tahoma", 7.5f), Text = "--\n--\n--\n--\n--\n--/--\n--/--" };
        panel.Controls.Add(_statsLabel);
        var close = MakeHotspot(8, 39, 16, 23);
        close.Click += (_, _) => { if (_characterWindow != null) _characterWindow.Visible = false; };
        panel.Controls.Add(close); close.BringToFront();
        return panel;
    }

    private Panel BuildSkillWindow()
    {
        var panel = new Panel { Width = 232, Height = 325, BackColor = Color.Black, BackgroundImage = LoadUiFrame(370), BackgroundImageLayout = ImageLayout.None };
        var skillBoard = LoadUiFrame(383);
        if (skillBoard != null) panel.Controls.Add(new PictureBox { Image = skillBoard, Bounds = new Rectangle(38, 52, skillBoard.Width, skillBoard.Height), SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent, Enabled = false });
        for (var row = 0; row < 5; row++)
        {
            var name = new Label { Bounds = new Rectangle(123, 114 + row * 37, 94, 18), ForeColor = Color.Silver, BackColor = Color.Transparent, Font = new Font("Tahoma", 8f), AutoEllipsis = true };
            _skillRows.Add(name);
            panel.Controls.Add(name);
            name.BringToFront();
        }
        var close = MakeHotspot(8, 39, 16, 23);
        close.Click += (_, _) => { if (_skillWindow != null) _skillWindow.Visible = false; };
        panel.Controls.Add(close); close.BringToFront();
        return panel;
    }

    private Label MakeSlotLabel() => new() { Dock = DockStyle.Fill, Text = string.Empty, ForeColor = Color.FromArgb(238, 219, 170), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

    private Image? LoadUiImage(string name)
    {
        try { using var source = Image.FromFile(Path.Combine(FindUiAssetRoot(), "classic-ui", name)); return new Bitmap(source); }
        catch { return null; }
    }

    private Image? LoadItemIcon(int looks)
    {
        if (_itemIcons.TryGetValue(looks, out var image)) return image;
        try { using var source = Image.FromFile(Path.Combine(FindUiAssetRoot(), "item-icons", $"{looks}.png")); image = new Bitmap(source); }
        catch { image = null; }
        _itemIcons[looks] = image; return image;
    }

    private Panel BuildChatPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(17, 27, 43), Padding = new Padding(6) };
        _chatLog = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(13, 19, 27), ForeColor = Color.FromArgb(220, 216, 199), BorderStyle = BorderStyle.None, IntegralHeight = false };
        _chatLog.Items.Add("左键点地移动，左键点怪物攻击；F9 背包，F10 人物，F11 技能。");
        _chatInput = new TextBox { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(31, 44, 64), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "输入聊天内容，回车发送" };
        panel.Controls.Add(_chatLog); panel.Controls.Add(_chatInput); return panel;
    }

    private void RefreshCharacterPanels()
    {
        if (IsDisposed || _world == null) return;
        if (InvokeRequired) { BeginInvoke(new Action(RefreshCharacterPanels)); return; }
        _bottomBoard?.Invalidate();
        if (_vitalsLabel != null) _vitalsLabel.Text = _world.HpText;
        if (_mpLabel != null) _mpLabel.Text = _world.MpText;
        if (_sidebarStatsLabel != null) _sidebarStatsLabel.Text = _world.CombatStats;
        if (_statsLabel != null) _statsLabel.Text = string.Join(Environment.NewLine, _world.Stats.Ac, _world.Stats.Mac, _world.Stats.Dc, _world.Stats.Mc, _world.Stats.Sc, $"{_world.CurrentHp}/{_world.MaximumHp}", $"{_world.CurrentMp}/{_world.MaximumMp}");
        RefreshPaperdoll();
        for (var i = 0; i < _bagSlots.Count; i++)
        {
            var slot = _bagSlots[i];
            if (i < _world.BagItems.Count) { var item = _world.BagItems[i]; slot.Text = ""; slot.Tag = item; slot.BackgroundImage = LoadItemIcon(item.Looks); slot.BackgroundImageLayout = ImageLayout.Zoom; slot.AccessibleDescription = $"{item.Name}，幸运 +{item.Luck}"; _itemTips.SetToolTip(slot, $"{item.Name}  幸运+{item.Luck}"); }
            else { slot.Text = ""; slot.Tag = null; slot.BackgroundImage = null; slot.AccessibleDescription = null; _itemTips.SetToolTip(slot, ""); }
        }
        var slotNames = new[] { "衣服", "武器", "照明", "项链", "头盔", "左手镯", "右手镯", "左戒指", "右戒指", "护身符", "腰带", "鞋", "宝石" };
        for (var i = 0; i < _equipmentSlots.Length; i++)
        {
            var label = _equipmentSlots[i]; if (label == null) continue;
            if (_world.Equipment.TryGetValue(i, out var item))
            {
                label.Text = string.Empty;
                label.BackgroundImage = i >= 2 ? LoadItemIcon(item.Looks) : null;
                label.BackgroundImageLayout = ImageLayout.Zoom;
                _itemTips.SetToolTip(label, $"{slotNames[i]}：{item.Name}  幸运+{item.Luck}");
            }
            else
            {
                label.Text = string.Empty;
                label.BackgroundImage = null;
                _itemTips.SetToolTip(label, $"{slotNames[i]}：空");
            }
        }
        for (var i = 0; i < _skillRows.Count; i++)
            _skillRows[i].Text = i < _world.Skills.Count ? _world.Skills[i] : string.Empty;
    }

    internal void ShowCharacterScreenPreview()
    {
        var sample = new CharacterInfo("probealpha", 0, 0, 12, 0);
        var list = new List<CharacterInfo> { sample };
        _characters.Tag = (list, 0);
        _selectName[0]!.Text = sample.Name;
        _selectLevel[0]!.Text = sample.Level.ToString();
        _selectJob[0]!.Text = sample.JobName;
        AddSelectPortrait(0, sample);
        _loginPanel.Visible = false;
        _characters.Visible = true;
        _characters.BringToFront();
    }

    internal void ShowWorldScreenPreview()
    {
        _activeCharacter = new CharacterInfo("probealpha", 0, 0, 12, 0);
        _world = new WorldView(FindGameDataRoot());
        _world.Accept(new ServerPacket(0, 51, 0, 0, 0, Encoding.ASCII.GetBytes("0"), []));
        var actor = new byte[12];
        _world.Accept(new ServerPacket(1, 50, 347, 349, 4, actor, []));
        var stats = new byte[40];
        stats[0] = 12;
        stats[2] = 1; stats[3] = 2;
        stats[4] = 0; stats[5] = 1;
        stats[6] = 2; stats[7] = 4;
        stats[8] = 0; stats[9] = 0;
        stats[10] = 1; stats[11] = 1;
        stats[12] = 80; stats[14] = 30; stats[16] = 100; stats[18] = 50;
        _world.Accept(new ServerPacket(1, 52, 347, 349, 0, stats, []));
        BuildWorldInterface(FindGameDataRoot());
    }

    private void RefreshPaperdoll()
    {
        if (_characterWindow == null || _world == null || _activeCharacter == null) return;
        foreach (var layer in _paperdollImages)
        {
            _characterWindow.Controls.Remove(layer);
            layer.Image?.Dispose();
            layer.Dispose();
        }
        _paperdollImages.Clear();

        var hairIndex = (_activeCharacter.Sex == 1 ? 441 : 440) + Math.Clamp(_activeCharacter.Hair / 2, 0, 11);
        AddPaperdollLayer(LoadUiFrame(hairIndex));
        if (_world.Equipment.TryGetValue(0, out var dress)) AddPaperdollLayer(LoadStateItemImage(dress.Looks));
        if (_world.Equipment.TryGetValue(1, out var weapon)) AddPaperdollLayer(LoadStateItemImage(weapon.Looks));
        if (_world.Equipment.TryGetValue(4, out var helmet)) AddPaperdollLayer(LoadStateItemImage(helmet.Looks));
        foreach (var slot in _equipmentSlots) slot?.BringToFront();
        _statsLabel?.BringToFront();
    }

    private void AddPaperdollLayer(Image? image)
    {
        if (image == null || _characterWindow == null) return;
        var layer = new PictureBox { Image = image, Bounds = new Rectangle(69, 148, image.Width, image.Height), SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent, Enabled = false };
        _paperdollImages.Add(layer);
        _characterWindow.Controls.Add(layer);
        layer.BringToFront();
    }

    private Image? LoadStateItemImage(int looks) => LoadUiImage(Path.Combine("frames", $"stateitem-{looks:000}.png"));

    private async Task ActivateBagItemAsync(WorldView.ItemView item)
    {
        try
        {
            var slot = EquipmentSlot(item.Mode);
            if (slot >= 0) await _client.ChangeEquipmentAsync(item.Id, slot, item.Name);
            else if (item.Mode is 0 or 1 or 2 or 3 or 4 or 31) await _client.UseItemAsync(item.Id, item.Name);
            else if (_worldStatus != null) _worldStatus.Text = "该物品不能直接穿戴或使用";
        }
        catch (Exception ex) { if (_worldStatus != null) _worldStatus.Text = ex.Message; }
    }

    private Image? LoadCharacterPortrait()
    {
        var character = _activeCharacter;
        if (character != null)
        {
            var portrait = LoadUiImage($"portrait-{character.Job}-{character.Sex}-{character.Hair}.png");
            if (portrait != null) return portrait;
            portrait = LoadUiImage($"portrait-{character.Job}-{character.Sex}.png");
            if (portrait != null) return portrait;
        }
        return LoadUiImage("portrait-0-0.png");
    }

    private void SelectBagItem(WorldView.ItemView item)
    {
        _selectedBagItem = item;
        if (_bagDetails != null) _bagDetails.Text = $"{item.Name}\r\nDC {item.Dc}  MC {item.Mc}  SC {item.Sc}  Luck +{item.Luck}";
    }

    private async Task UseSelectedBagItemAsync(WorldView.ItemView item)
    {
        if (EquipmentSlot(item.Mode) >= 0) { if (_worldStatus != null) _worldStatus.Text = "双击装备可直接穿戴。"; return; }
        try { await _client.UseItemAsync(item.Id, item.Name); }
        catch (Exception ex) { if (_worldStatus != null) _worldStatus.Text = ex.Message; }
    }

    private int EquipmentSlot(int mode)
    {
        if (mode is 24 or 26) return _world?.Equipment.ContainsKey(5) == true ? 6 : 5;
        if (mode is 22 or 23) return _world?.Equipment.ContainsKey(7) == true ? 8 : 7;
        return mode switch
        {
            10 or 11 => 0, 5 or 6 => 1, 28 or 29 or 30 => 2, 19 or 20 or 21 => 3, 15 => 4,
            25 or 51 => 9, 54 or 64 => 10, 52 or 62 => 11, 53 or 63 => 12, _ => -1
        };
    }

    private async Task RefreshInventoryAsync(uint actor, ushort x, ushort y)
    {
        try { await _client.QueryBagAsync(); await _client.QueryUserStateAsync(actor, x, y); }
        catch (Exception ex) { if (!IsDisposed && _worldStatus != null) BeginInvoke(new Action(() => _worldStatus.Text = ex.Message)); }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_world != null && keyData is Keys.F9 or Keys.K) { ToggleClassicWindow(_bagWindow); return true; }
        if (_world != null && keyData is Keys.F10 or Keys.J) { ToggleClassicWindow(_characterWindow); return true; }
        if (_world != null && keyData is Keys.F11 or Keys.L) { ToggleClassicWindow(_skillWindow); return true; }
        if (_world != null && keyData == Keys.Escape && (_bagWindow?.Visible == true || _characterWindow?.Visible == true || _skillWindow?.Visible == true))
        {
            if (_bagWindow != null) _bagWindow.Visible = false;
            if (_characterWindow != null) _characterWindow.Visible = false;
            if (_skillWindow != null) _skillWindow.Visible = false;
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleClassicWindow(Panel? panel)
    {
        if (panel == null || _viewportPanel == null) return;
        var show = !panel.Visible;
        foreach (var window in new[] { _characterWindow, _bagWindow, _skillWindow }) if (window != null) window.Visible = false;
        if (!show) return;
        panel.Location = panel == _bagWindow ? new Point(0, 0) : panel == _characterWindow ? new Point(800 - panel.Width, 52) : new Point(800 - panel.Width, 52);
        panel.Visible = true; panel.BringToFront();
    }

    private void RepositionClassicWindows()
    {
        if (_viewportPanel == null) return;
        foreach (var panel in new[] { _characterWindow, _bagWindow, _skillWindow })
            if (panel?.Visible == true) panel.Location = panel == _bagWindow ? new Point(0, 0) : new Point(800 - panel.Width, 52);
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _login.Enabled = !busy;
        _characters.Enabled = !busy;
        if (status != null) _status.Text = status;
    }

    private static string FindGameDataRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MIR2_GAMEDATA");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "scene.json")))
            return Path.GetFullPath(configured);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "GameData");
            if (File.Exists(Path.Combine(candidate, "scene.json"))) return candidate;
        }
        throw new DirectoryNotFoundException("找不到 GameData/scene.json。请从客户端代码目录运行程序，或将 GameData 放在客户端旁边。");
    }

    private static string FindUiAssetRoot()
    {
        var configured = Environment.GetEnvironmentVariable("MIR2_GAMEDATA");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "classic-ui", "login.png")))
            return Path.GetFullPath(configured);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var folder in new[] { "Assets", "GameData" })
            {
                var candidate = Path.Combine(dir.FullName, folder);
                if (File.Exists(Path.Combine(candidate, "classic-ui", "login.png"))) return candidate;
            }
        }
        throw new DirectoryNotFoundException("找不到客户端界面资源。");
    }
}
