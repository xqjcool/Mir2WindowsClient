using System.Text;
using System.Text.RegularExpressions;

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
    private readonly Dictionary<int, Image?> _stateItemImages = [];
    private readonly List<Image> _localizedCharacterArt = [];
    private readonly List<(Control Control, Point Position)> _worldHudControls = [];
    private Label? _vitalsLabel;
    private Label? _statsLabel;
    private Label? _sidebarStatsLabel;
    private Label? _mpLabel;
    private readonly List<Label> _skillRows = [];
    private Panel? _createCharacterPanel;
    private Panel? _changePasswordPanel;
    private Panel? _newAccountPanel;
    private readonly List<TextBox> _newAccountInputs = [];
    private Label? _newAccountStatus;
    private TextBox? _newCharacterName;
    private int _newCharacterSlot = -1;
    private int _newCharacterJob;
    private int _newCharacterSex;
    private int _newCharacterHair = 1;
    private readonly List<Button> _newCharacterJobButtons = [];
    private readonly List<Button> _newCharacterSexButtons = [];
    private ListBox? _chatLog;
    private TextBox? _chatInput;
    private Panel? _viewportPanel;
    private Panel? _characterWindow;
    private Panel? _bagWindow;
    private Panel? _skillWindow;
    private Panel? _npcDialogPanel;
    private Label? _npcDialogText;
    private FlowLayoutPanel? _npcDialogOptions;
    private uint _activeNpcId;
    private Label? _bagDetails;
    private WorldView.ItemView? _selectedBagItem;
    private bool _selfStateRequested;

    public Form1()
    {
        Text = "Mir 1.76";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(800, 600);
        MinimumSize = SizeFromClientSize(new Size(800, 600));
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
            BeginInvoke(new Action(() =>
            {
                try { _world?.Accept(packet); }
                catch (Exception ex)
                {
                    LoginClient.RecordClientException($"WorldPacket id={packet.Id}", ex);
                    if (_worldStatus != null)
                    {
                        _worldStatus.ForeColor = Color.Gold;
                        _worldStatus.Text = $"处理服务器数据失败：{ex.Message}";
                        _worldStatus.Visible = true;
                    }
                }
            }));
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
        create.Click += (_, _) => ShowNewAccountPanel();
        var changePassword = MakeArtButton(53, 252 + 130, 173 + 207, 76, 32);
        changePassword.Click += (_, _) => ShowChangePasswordPanel();
        var close = MakeArtButton(64, 252 + 252, 173 + 28, 18, 20);
        close.Click += (_, _) => Close();
        _loginPanel.Controls.Add(create);
        _loginPanel.Controls.Add(changePassword);
        _loginPanel.Controls.Add(close);
        // Keep protocol errors below the login button. The old coordinates
        // placed the message behind the button, making a rejected login look
        // like the OK button did nothing.
        _status.SetBounds(270, 418, 500, 48);
        _status.AutoSize = false;
        _status.ForeColor = Color.Gold;
        _status.BackColor = Color.Transparent;
        _status.Font = new Font("Tahoma", 8.5f);
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _loginPanel.Controls.Add(_status);
        _login.BringToFront();
        _account.BringToFront();
        _password.BringToFront();
        _status.BringToFront();
    }

    private void BuildOriginalCharacterScreen()
    {
        var background = LoadUiImage("characters.png");
        if (background != null) _characters.BackgroundImage = background;
        _characters.BackgroundImageLayout = ImageLayout.None;

        AddLocalizedArt(_characters, 66, 133, 453, "选择");
        AddLocalizedArt(_characters, 67, 685, 454, "选择");
        AddLocalizedArt(_characters, 68, 385, 456, "开始");
        AddLocalizedArt(_characters, 69, 348, 486, "创建角色");
        AddLocalizedArt(_characters, 70, 347, 506, "删除角色");
        AddLocalizedArt(_characters, 72, 379, 547, "退出");

        var firstSelect = MakeHotspot(133, 453, 76, 31);
        var secondSelect = MakeHotspot(685, 454, 76, 31);
        firstSelect.Click += (_, _) => SelectCharacterSlot(0);
        secondSelect.Click += (_, _) => SelectCharacterSlot(1);
        var create = MakeHotspot(347, 486, 196, 18);
        create.Click += (_, _) => ShowCreateCharacter();
        var delete = MakeHotspot(347, 506, 196, 18);
        delete.Click += async (_, _) => await DeleteSelectedCharacterAsync();
        var start = MakeHotspot(385, 456, 76, 17);
        start.Click += async (_, _) => await StartSelectedCharacterAsync();
        var exit = MakeHotspot(379, 547, 76, 24);
        exit.Click += (_, _) => { _characters.Visible = false; _loginPanel.Visible = true; _account.Focus(); };
        _characters.Controls.Add(firstSelect);
        _characters.Controls.Add(secondSelect);
        _characters.Controls.Add(create);
        _characters.Controls.Add(delete);
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

    private void AddLocalizedArt(Control parent, int frame, int x, int y, string text)
    {
        var source = LoadUiFrame(frame);
        if (source == null) return;
        var localized = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(localized))
        {
            graphics.DrawImageUnscaled(source, 0, 0);
            var inset = source.Height <= 17 ? 1 : 3;
            var sampleX = Math.Min(3, source.Width - 1);
            var sampleY = Math.Clamp(source.Height / 2, 0, source.Height - 1);
            using var background = new SolidBrush(source is Bitmap bitmap ? bitmap.GetPixel(sampleX, sampleY) : Color.FromArgb(45, 35, 27));
            graphics.FillRectangle(background, inset, inset, source.Width - inset * 2, source.Height - inset * 2);
            // Use the same type size and weight on every translated button.
            // The narrow original text strips (NEW/ERASE/EXIT) used to get a
            // smaller font solely because their source frames are shorter.
            using var font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            using var foreground = new SolidBrush(Color.FromArgb(255, 219, 158));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            var textBounds = new RectangleF(inset, inset, source.Width - inset * 2, source.Height - inset * 2);
            using var shadow = new SolidBrush(Color.FromArgb(190, 34, 20, 9));
            graphics.DrawString(text, font, shadow, new RectangleF(textBounds.X + 1, textBounds.Y + 1, textBounds.Width, textBounds.Height), format);
            graphics.DrawString(text, font, foreground, textBounds, format);
        }
        _localizedCharacterArt.Add(localized);
        parent.Controls.Add(new PictureBox { Bounds = new Rectangle(x, y, localized.Width, localized.Height), Image = localized, SizeMode = PictureBoxSizeMode.Normal, BackColor = Color.Transparent, Enabled = false });
    }

    private Image? LoadUiFrame(int frame)
    {
        if (_uiFrameCache.TryGetValue(frame, out var cached)) return cached;
        cached = LoadUiImage(Path.Combine("frames", $"prguse-{frame:000}.png"));
        _uiFrameCache[frame] = cached;
        return cached;
    }

    private void PaintOriginalBottomBoard(Graphics graphics)
    {
        if (_world == null) return;
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

    private void ShowLoginMessage(string message)
    {
        _status.Text = message;
        _status.SetBounds(270, 418, 500, 48);
        _status.BringToFront();
    }

    private void ShowChangePasswordPanel()
    {
        _changePasswordPanel?.Dispose();
        var panel = new Panel
        {
            Bounds = new Rectangle(192, 150, 416, 299),
            BackgroundImage = LoadUiFrame(50),
            BackgroundImageLayout = ImageLayout.None,
            BackColor = Color.Black
        };
        _changePasswordPanel = panel;
        var account = MakePasswordEdit(118, masked: false);
        var current = MakePasswordEdit(150, masked: true);
        var next = MakePasswordEdit(177, masked: true);
        var repeat = MakePasswordEdit(208, masked: true);
        panel.Controls.AddRange([account, current, next, repeat]);

        var result = new Label { Bounds = new Rectangle(20, 226, 376, 20), ForeColor = Color.Gold, BackColor = Color.Transparent, Font = new Font("Tahoma", 8f), TextAlign = ContentAlignment.MiddleCenter };
        panel.Controls.Add(result);
        var ok = MakeHotspot(181, 252, 77, 32);
        ok.Click += async (_, _) =>
        {
            if (next.Text != repeat.Text) { result.Text = "两次输入的新密码不一致。"; next.Focus(); return; }
            try
            {
                ok.Enabled = false;
                await _client.ChangePasswordAsync(_host.Text.Trim(), account.Text.Trim(), current.Text, next.Text);
                CloseChangePasswordPanel();
                ShowLoginMessage("密码修改成功。");
            }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { if (!ok.IsDisposed) ok.Enabled = true; }
        };
        var cancel = MakeHotspot(275, 251, 95, 33);
        cancel.Click += (_, _) => CloseChangePasswordPanel();
        panel.Controls.Add(ok); panel.Controls.Add(cancel);
        _loginPanel.Controls.Add(panel);
        panel.BringToFront();
        account.Focus();
    }

    private static TextBox MakePasswordEdit(int y, bool masked) => new()
    {
        Bounds = new Rectangle(237, y, 137, 16),
        MaxLength = 10,
        UseSystemPasswordChar = masked,
        BorderStyle = BorderStyle.None,
        BackColor = Color.Black,
        ForeColor = Color.White,
        Font = new Font("Microsoft Sans Serif", 10f)
    };

    private void CloseChangePasswordPanel()
    {
        if (_changePasswordPanel == null) return;
        _loginPanel.Controls.Remove(_changePasswordPanel);
        _changePasswordPanel.Dispose();
        _changePasswordPanel = null;
        _account.Focus();
    }

    private void ShowNewAccountPanel()
    {
        CloseNewAccountPanel();
        var background = LoadUiFrame(63);
        if (background == null) { ShowLoginMessage("缺少原版账号注册窗口素材 prguse-063.png。"); return; }
        var panel = new Panel
        {
            Bounds = new Rectangle((800 - background.Width) / 2, (600 - background.Height) / 2, background.Width, background.Height),
            BackgroundImage = background,
            BackgroundImageLayout = ImageLayout.None,
            BackColor = Color.Black
        };
        _newAccountPanel = panel;
        _newAccountInputs.Clear();
        AddRegistrationField(panel, 161, 117, 116, 10);
        AddRegistrationField(panel, 161, 138, 116, 10, true);
        AddRegistrationField(panel, 162, 159, 116, 10, true);
        AddRegistrationField(panel, 161, 188, 116, 20);
        AddRegistrationField(panel, 161, 207, 116, 18);
        AddRegistrationField(panel, 161, 227, 116, 10);
        AddRegistrationField(panel, 161, 257, 162, 20);
        AddRegistrationField(panel, 161, 277, 162, 12);
        AddRegistrationField(panel, 161, 297, 162, 20);
        AddRegistrationField(panel, 161, 317, 162, 12);
        AddRegistrationField(panel, 161, 348, 116, 14);
        AddRegistrationField(panel, 161, 368, 116, 13);
        AddRegistrationField(panel, 161, 387, 162, 40);

        _newAccountStatus = new Label { Bounds = new Rectangle(24, background.Height - 35, background.Width - 48, 18), ForeColor = Color.Gold, BackColor = Color.Transparent, Font = new Font("Tahoma", 8f), TextAlign = ContentAlignment.MiddleCenter };
        panel.Controls.Add(_newAccountStatus);
        var submit = MakeHotspot(157, 415, 76, 32);
        submit.Click += async (_, _) => await SubmitNewAccountAsync(submit);
        var cancel = MakeHotspot(445, 418, 86, 32);
        cancel.Click += (_, _) => CloseNewAccountPanel();
        var close = MakeHotspot(587, 33, 16, 23);
        close.Click += (_, _) => CloseNewAccountPanel();
        panel.Controls.Add(submit); panel.Controls.Add(cancel); panel.Controls.Add(close);
        _loginPanel.Controls.Add(panel);
        panel.BringToFront();
        _newAccountInputs[0].Focus();
    }

    private void AddRegistrationField(Panel panel, int x, int y, int width, int maxLength, bool masked = false)
    {
        var edit = new TextBox
        {
            Bounds = new Rectangle(x, y, width, 16),
            MaxLength = maxLength,
            UseSystemPasswordChar = masked,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Black,
            ForeColor = Color.White,
            Font = new Font("Microsoft Sans Serif", 10f)
        };
        _newAccountInputs.Add(edit);
        panel.Controls.Add(edit);
    }

    private async Task SubmitNewAccountAsync(Button submit)
    {
        if (_newAccountInputs.Count != 13 || _newAccountStatus == null) return;
        var value = _newAccountInputs.Select(input => input.Text.Trim()).ToArray();
        if (value[0].Length < 3) { _newAccountStatus.Text = "账号至少需要 3 个字符。"; _newAccountInputs[0].Focus(); return; }
        if (value[1].Length < 3) { _newAccountStatus.Text = "密码至少需要 3 个字符。"; _newAccountInputs[1].Focus(); return; }
        if (value[1] != value[2]) { _newAccountStatus.Text = "两次输入的密码不一致。"; _newAccountInputs[2].Focus(); return; }
        if (new[] { value[3], value[4], value[5], value[6], value[7], value[8], value[9] }.Any(string.IsNullOrWhiteSpace))
        { _newAccountStatus.Text = "请填写姓名、身份证号、生日和两组密保问题及答案。"; return; }
        var birthday = value[5].Split('/');
        if (birthday.Length != 3 || !int.TryParse(birthday[0], out var year) || !int.TryParse(birthday[1], out var month) || !int.TryParse(birthday[2], out var day)
            || year is <= 1890 or > 2101 || month is < 1 or > 12 || day is < 1 or > 31)
        { _newAccountStatus.Text = "生日格式请使用 年/月/日。"; _newAccountInputs[5].Focus(); return; }

        try
        {
            submit.Enabled = false;
            _newAccountStatus.Text = "正在提交注册资料…";
            var registration = new AccountRegistration(value[0], value[1], value[3], value[4], value[10], value[6], value[7], value[12], value[8], value[9], value[5], value[11]);
            await _client.RegisterAccountAsync(_host.Text.Trim(), registration);
            _account.Text = value[0];
            CloseNewAccountPanel();
            ShowLoginMessage("账号创建成功，请登录。");
            _password.Focus();
        }
        catch (Exception ex) { if (_newAccountStatus != null) _newAccountStatus.Text = ex.Message; }
        finally { if (!submit.IsDisposed) submit.Enabled = true; }
    }

    private void CloseNewAccountPanel()
    {
        if (_newAccountPanel == null) return;
        _loginPanel.Controls.Remove(_newAccountPanel);
        _newAccountPanel.Dispose();
        _newAccountPanel = null;
        _newAccountInputs.Clear();
        _newAccountStatus = null;
        _account.Focus();
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

    private void ShowCreateCharacter()
    {
        if (_characters.Tag is not ValueTuple<List<CharacterInfo>, int> selection || selection.Item1.Count >= 2)
        {
            _characterStatus!.Text = "一个账号最多创建两个角色。";
            return;
        }

        CloseCreateCharacter();
        _newCharacterSlot = selection.Item1.Count;
        _newCharacterJob = 0;
        _newCharacterSex = 0;
        _newCharacterHair = 1;

        // Original TSelectChrScene.MakeNewChar positions prguse-073 above the
        // slot being created, with its controls at these FState.pas offsets.
        var panel = new Panel
        {
            Bounds = new Rectangle(_newCharacterSlot == 0 ? 70 : 420, 15, 300, 417),
            BackgroundImage = LoadUiFrame(73),
            BackgroundImageLayout = ImageLayout.None,
            BackColor = Color.Black
        };
        _createCharacterPanel = panel;
        _newCharacterName = new TextBox { Bounds = new Rectangle(71, 107, 138, 21), MaxLength = 14, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(27, 22, 16), ForeColor = Color.White, Font = new Font("Microsoft Sans Serif", 10f) };
        _newCharacterName.TextChanged += (_, _) => RefreshNewCharacterPreview();
        panel.Controls.Add(_newCharacterName);

        for (var job = 0; job < 3; job++)
        {
            var selectedJob = job;
            var button = MakeArtButton(74 + job, 48 + job * 45, 157, 44, 36);
            button.Click += (_, _) => SelectNewCharacterJob(selectedJob);
            _newCharacterJobButtons.Add(button);
            panel.Controls.Add(button);
        }
        for (var sex = 0; sex < 2; sex++)
        {
            var selectedSex = sex;
            var button = MakeArtButton(77 + sex, 93 + sex * 45, 231, 44, 35);
            button.Click += (_, _) => SelectNewCharacterSex(selectedSex);
            _newCharacterSexButtons.Add(button);
            panel.Controls.Add(button);
        }

        var previousHair = MakeArtButton(79, 76, 308, 36, 27);
        previousHair.Click += (_, _) => { _newCharacterHair = (_newCharacterHair + 14) % 15; RefreshNewCharacterPreview(); };
        var nextHair = MakeArtButton(80, 170, 308, 36, 27);
        nextHair.Click += (_, _) => { _newCharacterHair = (_newCharacterHair + 1) % 15; RefreshNewCharacterPreview(); };
        var ok = MakeArtButton(62, 101, 359, 76, 32);
        ok.Click += async (_, _) => await CreateSelectedCharacterAsync();
        var close = MakeArtButton(64, 247, 30, 16, 23);
        close.Click += (_, _) => CloseCreateCharacter();
        panel.Controls.Add(previousHair); panel.Controls.Add(nextHair); panel.Controls.Add(ok); panel.Controls.Add(close);
        _characters.Controls.Add(panel);
        panel.BringToFront();
        _newCharacterName.Focus();
        RefreshNewCharacterPreview();
    }

    private void SelectNewCharacterJob(int job)
    {
        _newCharacterJob = job;
        UpdateNewCharacterChoiceHighlight();
        RefreshNewCharacterPreview();
    }

    private void SelectNewCharacterSex(int sex)
    {
        _newCharacterSex = sex;
        UpdateNewCharacterChoiceHighlight();
        RefreshNewCharacterPreview();
    }

    private void UpdateNewCharacterChoiceHighlight()
    {
        for (var i = 0; i < _newCharacterJobButtons.Count; i++)
            _newCharacterJobButtons[i].FlatAppearance.BorderSize = i == _newCharacterJob ? 1 : 0;
        for (var i = 0; i < _newCharacterSexButtons.Count; i++)
            _newCharacterSexButtons[i].FlatAppearance.BorderSize = i == _newCharacterSex ? 1 : 0;
    }

    private void RefreshNewCharacterPreview()
    {
        if (_newCharacterSlot < 0) return;
        var draft = new CharacterInfo(_newCharacterName?.Text ?? "", _newCharacterJob, _newCharacterHair, 1, _newCharacterSex);
        RemoveSelectPortrait(_newCharacterSlot);
        AddSelectPortrait(_newCharacterSlot, draft);
    }

    private async Task CreateSelectedCharacterAsync()
    {
        var name = _newCharacterName?.Text.Trim() ?? "";
        if (name.Length == 0) { _characterStatus!.Text = "请输入角色名。"; _newCharacterName?.Focus(); return; }
        SetBusy(true, "正在创建角色…");
        try
        {
            var characters = await _client.CreateCharacterAsync(name, _newCharacterHair, _newCharacterJob, _newCharacterSex);
            CloseCreateCharacter();
            RefreshCharacterSelection(characters, Math.Max(0, characters.Count - 1));
            _characterStatus!.Text = "角色创建成功。";
        }
        catch (Exception ex) { _characterStatus!.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private async Task DeleteSelectedCharacterAsync()
    {
        if (_characters.Tag is not ValueTuple<List<CharacterInfo>, int> selection || selection.Item2 >= selection.Item1.Count) return;
        var character = selection.Item1[selection.Item2];
        if (MessageBox.Show(this, $"确定删除角色“{character.Name}”吗？", "删除角色", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        SetBusy(true, "正在删除角色…");
        try
        {
            var characters = await _client.DeleteCharacterAsync(character.Name);
            RefreshCharacterSelection(characters, Math.Min(selection.Item2, Math.Max(0, characters.Count - 1)));
            _characterStatus!.Text = "角色已删除。";
        }
        catch (Exception ex) { _characterStatus!.Text = ex.Message; }
        finally { SetBusy(false); }
    }

    private void CloseCreateCharacter()
    {
        if (_createCharacterPanel != null)
        {
            _characters.Controls.Remove(_createCharacterPanel);
            _createCharacterPanel.Dispose();
            _createCharacterPanel = null;
        }
        if (_newCharacterSlot >= 0) RemoveSelectPortrait(_newCharacterSlot);
        _newCharacterName = null;
        _newCharacterSlot = -1;
        _newCharacterJobButtons.Clear();
        _newCharacterSexButtons.Clear();
    }

    private void RefreshCharacterSelection(IReadOnlyList<CharacterInfo> characters, int selectedIndex)
    {
        var list = characters.Take(2).ToList();
        var selected = list.Count == 0 ? 0 : Math.Clamp(selectedIndex, 0, list.Count - 1);
        _characters.Tag = (list, selected);
        for (var slot = 0; slot < 2; slot++)
        {
            var character = slot < list.Count ? list[slot] : null;
            _selectName[slot]!.Text = character?.Name ?? string.Empty;
            _selectLevel[slot]!.Text = character?.Level.ToString() ?? string.Empty;
            _selectJob[slot]!.Text = character?.JobName ?? string.Empty;
            RemoveSelectPortrait(slot);
            if (character != null) AddSelectPortrait(slot, character);
        }
        _characterStatus!.Text = list.Count == 0 ? "当前账号没有可进入的角色" : string.Empty;
        _characters.Invalidate();
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
            RefreshCharacterSelection(characters, 0);
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
        if (_world == null)
        {
            _world = new WorldView(assetRoot);
            _world.WorldActionDiagnostic += LoginClient.RecordWorldAction;
        }
        _world.Dock = DockStyle.Fill;
        _world.WalkRequested += async (x, y, direction) =>
        {
            try
            {
                await _client.WalkAsync(x, y, direction);
                LoginClient.RecordWorldAction($"wire-send command=3011 target=({x},{y}) direction={direction}");
                if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"移动请求已发送：{x},{y}")));
            }
            catch (Exception ex)
            {
                LoginClient.RecordClientException($"Walk target=({x},{y}) direction={direction}", ex);
                if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"移动请求失败：{ex.Message}")));
            }
        };
        _world.RunRequested += async (x, y, direction) =>
        {
            try
            {
                await _client.RunAsync(x, y, direction);
                LoginClient.RecordWorldAction($"wire-send command=3013 target=({x},{y}) direction={direction}");
                if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"跑步请求已发送：{x},{y}")));
            }
            catch (Exception ex)
            {
                LoginClient.RecordClientException($"Run target=({x},{y}) direction={direction}", ex);
                if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"跑步请求失败：{ex.Message}")));
            }
        };
        _world.AttackRequested += async (x, y, direction) =>
        {
            try { await _client.AttackAsync(x, y, direction); }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => { if (_worldStatus != null) _worldStatus.Text = ex.Message; })); }
        };
        _world.PickupRequested += async (x, y) =>
        {
            try
            {
                await _client.PickupAsync(x, y);
                if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"拾取请求已发送：{x},{y}")));
            }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"拾取请求失败：{ex.Message}"))); }
        };
        _world.NpcTalkRequested += async actor =>
        {
            try { await _client.TalkToNpcAsync(actor); }
            catch (Exception ex) { if (!IsDisposed) BeginInvoke(new Action(() => SetWorldStatus($"NPC 对话请求失败：{ex.Message}"))); }
        };
        _world.NpcDialogChanged += (actor, text) =>
        {
            if (!IsDisposed) BeginInvoke(new Action(() => ShowNpcDialog(actor, text)));
        };
        _world.NpcDialogClosed += () =>
        {
            if (!IsDisposed) BeginInvoke(new Action(() => { if (_npcDialogPanel != null) _npcDialogPanel.Visible = false; }));
        };
        var root = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _viewportPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _viewportPanel.Controls.Add(_world);
        _world.SetHudBoard(LoadUiFrame(1));
        _world.HudBoardPaint = PaintOriginalBottomBoard;

        // These controls are children of the original DBottom window. Its top
        // edge is y=349 in the 800x600 layout, so DFM Top values are relative
        // to that panel rather than to the screen.
        const int boardTop = 349;
        var stateButton = MakeArtButton(8, 643, boardTop + 61, 24, 24);
        _worldHudControls.Add((stateButton, new Point(643, 410)));
        stateButton.Click += (_, _) => ToggleClassicWindow(_characterWindow);
        var bagButton = MakeArtButton(9, 682, boardTop + 41, 24, 22);
        _worldHudControls.Add((bagButton, new Point(682, 390)));
        bagButton.Click += (_, _) => ToggleClassicWindow(_bagWindow);
        var magicButton = MakeArtButton(10, 722, boardTop + 21, 24, 23);
        _worldHudControls.Add((magicButton, new Point(722, 370)));
        magicButton.Click += (_, _) => ToggleClassicWindow(_skillWindow);
        var optionButton = MakeArtButton(11, 764, boardTop + 11, 24, 23);
        _worldHudControls.Add((optionButton, new Point(764, 360)));
        _itemTips.SetToolTip(optionButton, "退出游戏 (Esc)");
        optionButton.Click += (_, _) => PromptExitGame();
        foreach (var button in new[] { stateButton, bagButton, magicButton, optionButton })
        {
            _viewportPanel.Controls.Add(button);
            button.BringToFront();
        }

        var pickup = new Button { Text = "拾取", Width = 42, Height = 24, Left = 745, Top = 568, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(35, 30, 25), ForeColor = Color.White, Font = new Font("Tahoma", 8f) };
        _worldHudControls.Add((pickup, new Point(745, 568)));
        pickup.FlatAppearance.BorderColor = Color.FromArgb(142, 119, 83);
        pickup.Click += (_, _) => _world?.PickupAtPlayer();
        _viewportPanel.Controls.Add(pickup);
        pickup.BringToFront();

        var worldStatus = new Label { Left = 209, Top = 472, Width = 390, Height = 18, ForeColor = Color.FromArgb(35, 31, 26), BackColor = Color.Transparent, Font = new Font("Tahoma", 8f), Text = string.Empty, Visible = false };
        _worldHudControls.Add((worldStatus, new Point(209, 472)));
        _worldStatus = worldStatus;
        _world.StatusChanged += text => { if (!IsDisposed) { worldStatus.Text = text; worldStatus.Visible = !string.IsNullOrWhiteSpace(text); } };
        _viewportPanel.Controls.Add(worldStatus);
        worldStatus.BringToFront();
        var chatPanel = BuildChatPanel();
        chatPanel.SetBounds(210, 480, 390, 120);
        _worldHudControls.Add((chatPanel, new Point(210, 480)));
        _viewportPanel.Controls.Add(chatPanel);
        chatPanel.BringToFront();
        _chatInput!.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            var message = _chatInput.Text.Trim();
            if (message.Length == 0) return;
            _chatInput.Clear();
            try
            {
                await _client.SayAsync(message);
                AppendChatLine($"{_activeCharacter?.Name ?? "我"}：{message}");
            }
            catch (Exception ex) { SetWorldStatus($"聊天发送失败：{ex.Message}"); }
        };
        _world.WorldChatReceived += AppendChatLine;
        _characterWindow = BuildCharacterWindow();
        _bagWindow = BuildBagWindow();
        _skillWindow = BuildSkillWindow();
        foreach (var popup in new[] { _characterWindow, _bagWindow, _skillWindow })
        {
            popup.Visible = false; popup.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            popup.Location = new Point(800 - popup.Width, 52);
            _viewportPanel.Controls.Add(popup);
        }
        _viewportPanel.SizeChanged += (_, _) =>
        {
            LayoutClassicWorldHud();
            RepositionClassicWindows();
        };
        root.Controls.Add(_viewportPanel);
        Controls.Add(root); root.BringToFront();
        _world.CharacterDataChanged += RefreshCharacterPanels;
        RefreshCharacterPanels();
        _characters.Visible = false;
        _loginPanel.Visible = false;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ClientSize = new Size(800, 600);
        LayoutClassicWorldHud();
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
        _statsLabel = new Label { Left = 12, Top = 268, Width = 208, Height = 48, ForeColor = Color.FromArgb(238, 219, 170), BackColor = Color.Transparent, Font = new Font("Microsoft YaHei UI", 7f), Text = "攻击 --  魔法 --  道术 --\n防御 --  魔防 --\n生命 --/--  魔法 --/--" };
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
        var panel = new Panel { BackColor = Color.White, Padding = Padding.Empty };
        _chatLog = new ListBox { Dock = DockStyle.Fill, BackColor = Color.White, ForeColor = Color.FromArgb(50, 42, 32), BorderStyle = BorderStyle.None, IntegralHeight = false, Font = new Font("Microsoft YaHei UI", 8f) };
        _chatInput = new TextBox { Dock = DockStyle.Bottom, Height = 23, BackColor = Color.White, ForeColor = Color.Black, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "点击此处聊天，回车发送" };
        panel.Controls.Add(_chatLog); panel.Controls.Add(_chatInput); return panel;
    }

    private void AppendChatLine(string text)
    {
        if (IsDisposed || _chatLog == null || string.IsNullOrWhiteSpace(text)) return;
        if (_chatLog.InvokeRequired) { BeginInvoke(new Action(() => AppendChatLine(text))); return; }
        _chatLog.Items.Add(text);
        while (_chatLog.Items.Count > 100) _chatLog.Items.RemoveAt(0);
        if (_chatLog.Items.Count > 0) _chatLog.TopIndex = _chatLog.Items.Count - 1;
    }

    private void RefreshCharacterPanels()
    {
        if (IsDisposed || _world == null) return;
        if (InvokeRequired) { BeginInvoke(new Action(RefreshCharacterPanels)); return; }
        _world.Invalidate();
        if (_vitalsLabel != null) _vitalsLabel.Text = _world.HpText;
        if (_mpLabel != null) _mpLabel.Text = _world.MpText;
        if (_sidebarStatsLabel != null) _sidebarStatsLabel.Text = _world.CombatStats;
        if (_statsLabel != null) _statsLabel.Text = $"攻击 {_world.Stats.Dc}  魔法 {_world.Stats.Mc}  道术 {_world.Stats.Sc}\n防御 {_world.Stats.Ac}  魔防 {_world.Stats.Mac}\n生命 {_world.CurrentHp}/{_world.MaximumHp}  魔法 {_world.CurrentMp}/{_world.MaximumMp}";
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
        // Exercise repeated paperdoll refreshes as the server's equipment
        // packet (751) arrives after initial login.
        for (var i = 0; i < 3; i++)
            _world.Accept(new ServerPacket(0, 751, 0, 0, 0, new byte[1688], []));
    }

    private void RefreshPaperdoll()
    {
        if (_characterWindow == null || _world == null || _activeCharacter == null) return;
        foreach (var layer in _paperdollImages)
        {
            _characterWindow.Controls.Remove(layer);
            // Images supplied by LoadUiFrame/LoadStateItemImage are shared
            // caches. Disposing them here invalidates the cache and the next
            // server status packet throws while reading Image.Width.
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

    private Image? LoadStateItemImage(int looks)
    {
        if (_stateItemImages.TryGetValue(looks, out var image)) return image;
        image = LoadUiImage(Path.Combine("frames", $"stateitem-{looks:000}.png"));
        _stateItemImages[looks] = image;
        return image;
    }

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
        if (_world != null && keyData == (Keys.Control | Keys.H))
        {
            _ = ToggleAttackModeAsync();
            return true;
        }
        if (_world != null && keyData is Keys.F9 or Keys.K) { ToggleClassicWindow(_bagWindow); return true; }
        if (_world != null && keyData is Keys.F10 or Keys.J) { ToggleClassicWindow(_characterWindow); return true; }
        if (_world != null && keyData is Keys.F11 or Keys.L) { ToggleClassicWindow(_skillWindow); return true; }
        if (_world != null && keyData == Keys.Escape)
        {
            var popupOpen = _bagWindow?.Visible == true || _characterWindow?.Visible == true || _skillWindow?.Visible == true;
            if (popupOpen)
            {
                if (_bagWindow != null) _bagWindow.Visible = false;
                if (_characterWindow != null) _characterWindow.Visible = false;
                if (_skillWindow != null) _skillWindow.Visible = false;
            }
            else PromptExitGame();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task ToggleAttackModeAsync()
    {
        try { await _client.SayAsync("@AttackMode"); SetWorldStatus("已请求切换攻击模式"); }
        catch (Exception ex) { SetWorldStatus($"切换攻击模式失败：{ex.Message}"); }
    }

    private void ShowNpcDialog(uint actor, string text)
    {
        if (_viewportPanel == null) return;
        _activeNpcId = actor;
        if (_npcDialogPanel == null)
        {
            var panel = new Panel { Size = new Size(460, 270), BackColor = Color.FromArgb(20, 24, 31), BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(12) };
            _npcDialogText = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(244, 222, 177), Font = new Font("Microsoft YaHei UI", 10f), AutoEllipsis = true };
            _npcDialogOptions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 74, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, AutoScroll = true, BackColor = panel.BackColor };
            var close = new Button { Text = "关闭对话", Width = 88, Height = 28, Dock = DockStyle.Bottom, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(79, 55, 34) };
            close.Click += async (_, _) =>
            {
                try { await _client.SelectNpcOptionAsync(_activeNpcId, "@exit"); }
                catch (Exception ex) { SetWorldStatus($"关闭 NPC 对话失败：{ex.Message}"); }
                if (_npcDialogPanel != null) _npcDialogPanel.Visible = false;
            };
            panel.Controls.Add(_npcDialogText);
            panel.Controls.Add(_npcDialogOptions);
            panel.Controls.Add(close);
            _npcDialogPanel = panel;
            _viewportPanel.Controls.Add(panel);
        }

        _npcDialogText!.Text = Regex.Replace(text, "<([^<>/]+)/(@[^<>]+)>", "$1");
        _npcDialogOptions!.Controls.Clear();
        foreach (Match match in Regex.Matches(text, "<([^<>/]+)/(@[^<>]+)>"))
        {
            var label = match.Groups[1].Value.Trim();
            var command = match.Groups[2].Value.Trim();
            if (label.Length == 0 || command.Length == 0) continue;
            var option = new Button { Text = label, AutoSize = true, MinimumSize = new Size(80, 30), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(70, 54, 36), Margin = new Padding(3) };
            option.Click += async (_, _) =>
            {
                try { await _client.SelectNpcOptionAsync(_activeNpcId, command); }
                catch (Exception ex) { SetWorldStatus($"NPC 选项请求失败：{ex.Message}"); }
            };
            _npcDialogOptions.Controls.Add(option);
        }
        _npcDialogPanel!.Location = new Point(Math.Max(8, (_viewportPanel.ClientSize.Width - _npcDialogPanel.Width) / 2), Math.Max(8, (_viewportPanel.ClientSize.Height - _npcDialogPanel.Height) / 2));
        _npcDialogPanel.Visible = true;
        _npcDialogPanel.BringToFront();
    }

    private void PromptExitGame()
    {
        if (MessageBox.Show(this, "确定退出游戏吗？", "Mir 1.76", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Application.Exit();
    }

    private void ToggleClassicWindow(Panel? panel)
    {
        if (panel == null || _viewportPanel == null) return;
        var show = !panel.Visible;
        foreach (var window in new[] { _characterWindow, _bagWindow, _skillWindow }) if (window != null) window.Visible = false;
        if (!show) return;
        var (offsetX, offsetY) = WorldHudOrigin();
        panel.Location = panel == _bagWindow ? new Point(offsetX, offsetY) : new Point(offsetX + 800 - panel.Width, offsetY + 52);
        panel.Visible = true; panel.BringToFront();
    }

    private (int X, int Y) WorldHudOrigin()
    {
        if (_viewportPanel == null) return (0, 0);
        return (Math.Max(0, (_viewportPanel.ClientSize.Width - 800) / 2), Math.Max(0, _viewportPanel.ClientSize.Height - 600));
    }

    private void LayoutClassicWorldHud()
    {
        if (_viewportPanel == null) return;
        var (offsetX, offsetY) = WorldHudOrigin();
        foreach (var (control, position) in _worldHudControls)
            if (!control.IsDisposed) control.Location = new Point(offsetX + position.X, offsetY + position.Y);
    }

    private void RepositionClassicWindows()
    {
        if (_viewportPanel == null) return;
        var (offsetX, offsetY) = WorldHudOrigin();
        foreach (var panel in new[] { _characterWindow, _bagWindow, _skillWindow })
            if (panel?.Visible == true) panel.Location = panel == _bagWindow ? new Point(offsetX, offsetY) : new Point(offsetX + 800 - panel.Width, offsetY + 52);
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
