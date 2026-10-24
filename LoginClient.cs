using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace MirWindowsClient;

public sealed record CharacterInfo(string Name, int Job, int Hair, int Level, int Sex)
{
    public string JobName => Job switch { 0 => "战士", 1 => "法师", 2 => "道士", _ => "未知职业" };
}

internal sealed record ServerPacket(uint Actor, ushort Id, ushort X, ushort Y, ushort Series, byte[] Body, IReadOnlyList<byte[]> BodyParts);

/// <summary>Classic Mir2 EDCode framing and the LoginGate -> SelGate -> GameGate login flow.</summary>
internal sealed class LoginClient : IDisposable
{
    private static readonly Encoding LegacyEncoding = CreateLegacyEncoding();
    private static readonly object DiagnosticLock = new();
    private volatile bool _diagnosticsActive = true;
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionCancellation;
    private Task? _reader;
    private Task? _worldReader;
    private Channel<ServerPacket> _packets = Channel.CreateUnbounded<ServerPacket>();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private byte _sequence;
    private string _account = "";
    private string _ticket = "";
    private readonly List<ServerPacket> _initialWorldPackets = [];

    public event Action<ServerPacket>? WorldPacketReceived;
    public event Action<Exception>? WorldConnectionEnded;

    private static Encoding CreateLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private void Diagnostic(string message)
    {
        if (!_diagnosticsActive) return;
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MirWindowsClient");
            Directory.CreateDirectory(directory);
            lock (DiagnosticLock) File.AppendAllText(Path.Combine(directory, "protocol.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }

    public async Task<IReadOnlyList<CharacterInfo>> LoginAsync(string host, string account, string password, string serverName)
    {
        _diagnosticsActive = true;
        _initialWorldPackets.Clear();
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("请填写服务器地址、账号和密码。");
        if (new[] { account, password, serverName }.Any(s => s.Contains('/') || s.Contains('\0')))
            throw new InvalidOperationException("账号、密码或服务器名称含有协议分隔字符。");
        _account = account;

        await ConnectGateAsync(host, 7000);
        await SendCommandAsync(2001, body: account + "/" + password);
        var login = await WaitForAsync("LoginGate 账号验证", 529, 503);
        if (login.Id == 503)
        {
            if (login.Actor == unchecked((uint)-3))
                throw new InvalidOperationException("该账号已有登录会话。请先退出其他客户端，或等待服务端清理残留会话后重试。");
            throw new InvalidOperationException("账号或密码错误，或账号暂时不可用。");
        }

        await SendCommandAsync(104, body: serverName);
        var server = await WaitForAsync("LoginSrv 区服列表", 530, 503);
        if (server.Id != 530) throw new InvalidOperationException("登录服务器没有返回可用区服。");
        var fields = LegacyEncoding.GetString(server.Body).Split('/');
        if (fields.Length < 3 || !ushort.TryParse(fields[1], out var selectPort) || selectPort != 7100 || string.IsNullOrEmpty(fields[2]))
            throw new InvalidOperationException("区服返回的数据格式不匹配；请检查服务器地址配置。");
        _ticket = fields[2];

        await ConnectGateAsync(host, selectPort);
        await SendCommandAsync(100, body: account + "/" + _ticket);
        var list = await WaitForAsync("SelGate 角色列表", 520, 526, 527);
        if (list.Id != 520) throw new InvalidOperationException("角色服务器拒绝了本次登录。");
        return ParseCharacters(list.Actor, list.Body);
    }

    public async Task EnterWorldAsync(string character)
    {
        if (string.IsNullOrWhiteSpace(character) || character.Contains('/'))
            throw new InvalidOperationException("角色名无效。");
        await SendCommandAsync(103, body: _account + "/" + character);
        var selected = await WaitForAsync("SelGate 角色认证", 525, 526, 527);
        if (selected.Id != 525) throw new InvalidOperationException("角色认证失败，请确认角色未在其他客户端登录。");

        var host = ((IPEndPoint)_tcp!.Client.RemoteEndPoint!).Address.ToString();
        await ConnectGateAsync(host, 7200);
        await SendRawAsync(Encode(LegacyEncoding.GetBytes($"**{_account}/{character}/{_ticket}/1200409180/1200409180")));
        var notice = await WaitForAsync("GameGate 登录公告", 658, 526, 527);
        if (notice.Id != 658)
            throw new InvalidOperationException("GameGate 拒绝了角色登录请求。");

        // The original client acknowledges SM_SENDNOTICE before GameSrv sends SM_LOGON.
        await SendCommandAsync(1018, actor: unchecked((uint)Environment.TickCount));
        var entered = await WaitForAsync("GameGate 进入游戏", 50, 526, 527, preserveWorldPackets: true);
        if (entered.Id != 50) throw new InvalidOperationException("游戏服务器拒绝了角色登录。");
    }

    public async Task StartWorldAsync()
    {
        await SendCommandAsync(81);
        foreach (var packet in _initialWorldPackets) WorldPacketReceived?.Invoke(packet);
        _initialWorldPackets.Clear();
        _diagnosticsActive = false;
        _worldReader = Task.Run(async () =>
        {
            try
            {
                await foreach (var packet in _packets.Reader.ReadAllAsync())
                    WorldPacketReceived?.Invoke(packet);
                WorldConnectionEnded?.Invoke(new EndOfStreamException("服务器已关闭连接。"));
            }
            catch (Exception ex) { WorldConnectionEnded?.Invoke(ex); }
        });
    }

    public Task QueryUserStateAsync(uint actor, ushort x, ushort y) => SendCommandAsync(82, actor: actor, x: x, y: y);
    public Task QueryBagAsync() => SendCommandAsync(81);
    public Task ChangeEquipmentAsync(int itemId, int slot, string itemName) => SendCommandAsync(1003, actor: unchecked((uint)itemId), x: checked((ushort)slot), body: itemName);
    public Task UseItemAsync(int itemId, string itemName) => SendCommandAsync(1006, actor: unchecked((uint)itemId), body: itemName);
    public Task SayAsync(string text) => SendCommandAsync(3030, body: text);

    public Task WalkAsync(int x, int y, int direction)
    {
        if (x is < 0 or > ushort.MaxValue || y is < 0 or > ushort.MaxValue || direction is < 0 or > 7)
            throw new ArgumentOutOfRangeException(nameof(x));
        // The classic client sends the destination packed into Recog and direction in Y.
        var actor = (uint)x | ((uint)y << 16);
        return SendCommandAsync(3011, actor: actor, y: (ushort)direction);
    }

    public Task PickupAsync(int x, int y) => SendCommandAsync(1001, x: checked((ushort)x), y: checked((ushort)y));

    public Task AttackAsync(int x, int y, int direction) =>
        SendCommandAsync(3014, actor: (uint)x | ((uint)y << 16), y: checked((ushort)direction));

    private async Task ConnectGateAsync(string host, ushort port)
    {
        await CloseGateAsync();
        _packets = Channel.CreateUnbounded<ServerPacket>();
        _sequence = 0;
        var tcp = new TcpClient { NoDelay = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try { await tcp.ConnectAsync(host, port, timeout.Token); }
        catch (Exception ex) { tcp.Dispose(); throw new IOException($"无法连接 {host}:{port}。请确认服务器和端口已启动。", ex); }
        Diagnostic($"connected endpoint={host}:{port}");
        _tcp = tcp;
        _stream = tcp.GetStream();
        _connectionCancellation = new CancellationTokenSource();
        _reader = ReceiveLoopAsync(_connectionCancellation.Token);
    }

    private async Task CloseGateAsync()
    {
        _packets.Writer.TryComplete();
        if (_connectionCancellation != null)
        {
            await _connectionCancellation.CancelAsync();
            _tcp?.Dispose();
            if (_reader != null) { try { await _reader; } catch { } }
            _connectionCancellation.Dispose();
        }
        _connectionCancellation = null;
        _reader = null;
        _stream = null;
        _tcp = null;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var stream = _stream!;
        var buffer = new byte[8192];
        var frame = new List<byte>();
        var inFrame = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var count = await stream.ReadAsync(buffer, cancellationToken);
                if (count == 0) throw new EndOfStreamException("服务器关闭了连接。");
                Diagnostic($"received chunk bytes={count}");
                for (var i = 0; i < count; i++)
                {
                    var value = buffer[i];
                    if (value == (byte)'*')
                    {
                        await stream.WriteAsync(new byte[] { (byte)'*' }, cancellationToken);
                        continue;
                    }
                    if (value == (byte)'#') { inFrame = true; frame.Clear(); continue; }
                    if (!inFrame) continue;
                    if (value == (byte)'!')
                    {
                        inFrame = false;
                        if (frame.Count > 0)
                        {
                            try
                            {
                                // Client requests include a sequence after '#', but LoginSrv's
                                // replies are framed as "#" + EDCode payload + "!$" with no sequence.
                                // EDCode bytes are 60..123, so an initial ASCII digit unambiguously
                                // identifies the optional client-style sequence.
                                var hasSequence = frame[0] is >= (byte)'0' and <= (byte)'9';
                                var payloadOffset = hasSequence ? 1 : 0;
                                var packet = DecodePacket(frame.Skip(payloadOffset).ToArray());
                                Diagnostic($"received frame sequence={(hasSequence ? ((char)frame[0]).ToString() : "none")} encodedBytes={frame.Count - payloadOffset} packetId={packet.Id}");
                                await _packets.Writer.WriteAsync(packet, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                Diagnostic($"discarded frame bytes={frame.Count} firstByte={(int)frame[0]} error={ex.GetType().Name}");
                                _packets.Writer.TryComplete(new InvalidDataException("服务器响应无法解析。", ex));
                                return;
                            }
                        }
                        frame.Clear();
                        continue;
                    }
                    // The first byte after '#' is the classic frame sequence ('0'..'9'),
                    // which sits outside the EDCode alphabet. Keep it for validation below.
                    if (frame.Count == 0 && value is >= (byte)'0' and <= (byte)'9')
                    {
                        frame.Add(value);
                        continue;
                    }
                    if (value is < 60 or > 123) { inFrame = false; frame.Clear(); continue; }
                    frame.Add(value);
                    if (frame.Count > 65536) throw new InvalidDataException("服务器数据帧超过长度限制。");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _packets.Writer.TryComplete(ex); }
    }

    private async Task<ServerPacket> WaitForAsync(string stage, ushort firstId, ushort secondId = ushort.MaxValue, ushort thirdId = ushort.MaxValue, bool preserveWorldPackets = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            ServerPacket packet;
            try { packet = await _packets.Reader.ReadAsync(timeout.Token); }
            catch (OperationCanceledException) { Diagnostic($"timeout stage={stage}"); throw new TimeoutException($"等待 {stage} 响应超时。请检查对应服务和网关日志。"); }
            catch (ChannelClosedException ex) { throw new IOException("登录连接已断开。", ex.InnerException ?? ex); }
            if (packet.Id == firstId || packet.Id == secondId || packet.Id == thirdId)
            {
                if (preserveWorldPackets) _initialWorldPackets.Add(packet);
                return packet;
            }
            if (preserveWorldPackets) _initialWorldPackets.Add(packet);
        }
    }

    private Task SendCommandAsync(ushort id, uint actor = 0, ushort x = 0, ushort y = 0, ushort series = 0, string body = "")
    {
        Diagnostic($"send command={id}");
        var header = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), actor);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), id);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), x);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), y);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), series);
        return SendRawAsync(Encode(header).Concat(Encode(LegacyEncoding.GetBytes(body))).ToArray());
    }

    private async Task SendRawAsync(byte[] payload)
    {
        var stream = _stream ?? throw new IOException("尚未连接服务器。");
        await _writeLock.WaitAsync();
        try
        {
            _sequence = (byte)(_sequence % 9 + 1);
            var frame = new byte[payload.Length + 3];
            frame[0] = (byte)'#'; frame[1] = (byte)('0' + _sequence);
            payload.CopyTo(frame, 2); frame[^1] = (byte)'!';
            await stream.WriteAsync(frame);
        }
        finally { _writeLock.Release(); }
    }

    private static IReadOnlyList<CharacterInfo> ParseCharacters(uint countRaw, byte[] body)
    {
        var count = checked((int)countRaw);
        if (count is < 0 or > 2) throw new InvalidDataException("角色数量超出当前客户端支持范围。");
        var text = LegacyEncoding.GetString(body);
        if (text.Length == 0 && count == 0) return Array.Empty<CharacterInfo>();
        var fields = text.Split('/').ToList();
        if (fields.Count > 0 && fields[^1].Length == 0) fields.RemoveAt(fields.Count - 1);
        if (fields.Count != count * 5) throw new InvalidDataException("角色列表格式不匹配。");
        var result = new List<CharacterInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var offset = i * 5;
            if (!int.TryParse(fields[offset + 1], out var job) || job is < 0 or > 2 ||
                !int.TryParse(fields[offset + 2], out var hair) || hair is < 0 or > 255 ||
                !int.TryParse(fields[offset + 3], out var level) || level < 0 ||
                !int.TryParse(fields[offset + 4], out var sex) || sex is < 0 or > 1)
                throw new InvalidDataException("角色资料格式不匹配。");
            var name = fields[offset].Trim('*');
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("服务器返回了空角色名。");
            result.Add(new CharacterInfo(name, job, hair, level, sex));
        }
        return result;
    }

    private static ServerPacket DecodePacket(byte[] frame)
    {
        if (frame.Length < 16) throw new InvalidDataException("消息头长度不足。");
        var header = Decode(frame.AsSpan(0, 16));
        if (header.Length != 12) throw new InvalidDataException("消息头格式不匹配。");
        var actor = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        var id = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4, 2));
        var x = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6, 2));
        var y = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8, 2));
        var series = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10, 2));
        var encodedBody = frame.AsSpan(16);
        byte[] body;
        IReadOnlyList<byte[]> parts;
        if (id == 17) { body = encodedBody.ToArray(); parts = [body]; } // Classic spell packet carries a plain decimal body.
        else if ((id == 10 || id == 50) && encodedBody.Length >= 11)
        {
            parts = [Decode(encodedBody[..11]), Decode(encodedBody[11..])];
            body = parts.SelectMany(p => p).ToArray(); // CharDesc and name are encoded as separate records.
            return new ServerPacket(actor, id, x, y, series, body, parts);
        }
        else if (id is 201 or 211 or 621)
        {
            var records = SplitRecords(encodedBody);
            if (records.Count > 0 && records[^1].Length == 0) records.RemoveAt(records.Count - 1);
            if (id == 621)
            {
                if (records.Count % 2 != 0) throw new InvalidDataException("装备列表格式错误。");
                var decoded = new List<byte[]>();
                for (var i = 0; i < records.Count; i += 2)
                {
                    if (records[i].Length == 0 || records[i].Any(b => b is < (byte)'0' or > (byte)'9')) throw new InvalidDataException("装备位置格式错误。");
                    decoded.Add(records[i]); decoded.Add(Decode(records[i + 1]));
                }
                parts = decoded;
            }
            else parts = records.Select(record => Decode(record)).ToArray();
            body = parts.SelectMany(p => p).ToArray();
        }
        else { body = Decode(encodedBody); parts = [body]; }
        return new ServerPacket(actor, id, x, y, series, body, parts);
    }

    private static List<byte[]> SplitRecords(ReadOnlySpan<byte> bytes)
    {
        var records = new List<byte[]>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'/') continue;
            records.Add(bytes[start..i].ToArray());
            start = i + 1;
        }
        if (start < bytes.Length) records.Add(bytes[start..].ToArray());
        else if (start == 0 && bytes.Length == 0) return records;
        else records.Add([]);
        return records;
    }

    private static byte[] Encode(ReadOnlySpan<byte> raw)
    {
        var output = new List<byte>(raw.Length * 4 / 3 + 4);
        var no = 2;
        var remainder = 0;
        foreach (var value in raw)
        {
            var c = value ^ 172;
            if (no == 6)
            {
                output.Add((byte)((c & 63) + 60));
                output.Add((byte)((remainder | ((c >> 2) & 48)) + 60));
                remainder = 0;
            }
            else
            {
                var t = c >> 2;
                output.Add((byte)(((t & 60) | (c & 3)) + 60));
                remainder = (remainder << 2) | (t & 3);
            }
            no = no % 6 + 2;
        }
        if (no != 2) output.Add((byte)(remainder + 60));
        return output.ToArray();
    }

    private static byte[] Decode(ReadOnlySpan<byte> encoded)
    {
        if (encoded.IsEmpty) return Array.Empty<byte>();
        if (encoded.ToArray().Any(b => b is < 60 or > 123)) throw new InvalidDataException("EDCode 数据超出合法范围。");
        var output = new List<byte>(encoded.Length * 3 / 4 + 2);
        for (var i = 0; i < encoded.Length; i += 4)
        {
            var count = Math.Min(4, encoded.Length - i);
            if (count < 2) throw new InvalidDataException("EDCode 分组不完整。");
            var b0 = encoded[i] - 60;
            var b1 = encoded[i + 1] - 60;
            var r = encoded[i + count - 1] - 60;
            var firstMiddle = count > 2 ? r & 12 : (r << 2) & 12;
            output.Add((byte)((((b0 << 2) & 240) | firstMiddle | (b0 & 3)) ^ 172));
            if (count > 2)
            {
                var second = (((b1 << 2) & 240) | ((r << 2) & 12) | (b1 & 3)) ^ 172;
                output.Add((byte)second);
            }
            if (count == 4) output.Add((byte)((encoded[i + 2] - 60 | ((r << 2) & 192)) ^ 172));
        }
        return output.ToArray();
    }

    public void Dispose()
    {
        try { CloseGateAsync().GetAwaiter().GetResult(); } catch { }
        _writeLock.Dispose();
    }
}
