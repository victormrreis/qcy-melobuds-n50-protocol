// Padrões de timeout WinRT GATT (TryOp) e aquecimento do cache LE via AdvertisementWatcher
// inspirados em JblTray (https://github.com/emperorbug46/JblTray, MIT License) e
// OpenQCY-Desktop (https://github.com/GiorgioRafael/OpenQCY-Desktop, MIT License).

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using N50.Core.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace N50.Probe;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        string cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "scan" => await RunScanAsync(args.Length > 1 && int.TryParse(args[1], out int s) ? s : 8),
                "diag" => await RunDiagAsync(),
                "probe" => await RunReadOnlyProbeAsync(await ResolveMacAsync(args.ElementAtOrDefault(1), preferClassic: false)),
                "probe-spp" => await RunSppReadOnlyProbeAsync(await ResolveMacAsync(args.ElementAtOrDefault(1), preferClassic: true)),
                "spp-set-anc" => await RunSppSetAncAsync(args),
                "spp-set-game" => await RunSppSetGameAsync(args),
                "spp-watch" => await RunSppWatchAsync(
                    args.Length > 1 && int.TryParse(args[1], out int sw) ? sw : 15,
                    await ResolveMacAsync(args.ElementAtOrDefault(2), preferClassic: true)),
                "set-anc" => await RunSetAncAsync(args),
                "set-game" => await RunSetGameAsync(args),
                "watch" => await RunWatchAsync(
                    args.Length > 1 && int.TryParse(args[1], out int ws) ? ws : 15,
                    await ResolveMacAsync(args.ElementAtOrDefault(2), preferClassic: false)),
                _ => PrintUsageAndFail()
            };
        }
        catch (Exception ex)
        {
            Log($"ERRO FATAL: {ex.GetType().Name}: {ex.Message}");
            return 2;
        }
    }

    private static int PrintUsageAndFail()
    {
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("N50.Probe — Ferramenta CLI de leitura/teste segura para QCY MeloBuds N50 (ProtocolGuard ativo)");
        Console.WriteLine("  scan [segundos=8]                             Leitura passiva de anuncios BLE 0x521C + pareados Windows");
        Console.WriteLine("  probe-spp [mac]                               Sondagem RFCOMM SPP 100% SOMENTE LEITURA (recomendado)");
        Console.WriteLine("  spp-watch [segundos=15] [mac]                 Escuta notificacoes espontaneas via RFCOMM SPP");
        Console.WriteLine("  spp-set-anc <mode> <scene> <lvl> [mac]        Escrita reversivel ANC via SPP (ex.: 01 05 00 | 03 01 04 | 02 00 00)");
        Console.WriteLine("  spp-set-game <on|off> [mac]                   Escrita reversivel Modo Jogo via SPP");
        Console.WriteLine("  probe [mac]                                   Sondagem BLE GATT A002 somente leitura");
        Console.WriteLine("  diag                                          Diagnostico de abertura BLE GATT vs pareados");
        Console.WriteLine("  (Se [mac] for omitido, descobre automaticamente o QCY MeloBuds N50 pareado no Windows.)");
    }

    private static ulong ParseMac(string text)
    {
        string clean = text.Replace(":", "").Replace("-", "").Trim();
        return ulong.Parse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static async Task<ulong> ResolveMacAsync(string? text, bool preferClassic)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            return ParseMac(text);
        }

        string selector = preferClassic
            ? BluetoothDevice.GetDeviceSelectorFromPairingState(true)
            : BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var (ok, devices) = await TryOp(DeviceInformation.FindAllAsync(selector), 6000);
        if (ok && devices is not null)
        {
            foreach (var d in devices)
            {
                if (!d.Name.Contains("N50", StringComparison.OrdinalIgnoreCase) &&
                    !d.Name.Contains("QCY", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (preferClassic)
                {
                    var (okBt, bt) = await TryOp(BluetoothDevice.FromIdAsync(d.Id), 5000);
                    if (okBt && bt is not null)
                    {
                        ulong addr = bt.BluetoothAddress;
                        bt.Dispose();
                        Log($"Descoberto automaticamente (Classic): '{d.Name}' ({FormatMac(addr)})");
                        return addr;
                    }
                }
                else
                {
                    var (okLe, le) = await TryOp(BluetoothLEDevice.FromIdAsync(d.Id), 5000);
                    if (okLe && le is not null)
                    {
                        ulong addr = le.BluetoothAddress;
                        le.Dispose();
                        Log($"Descoberto automaticamente (BLE): '{d.Name}' ({FormatMac(addr)})");
                        return addr;
                    }
                }
            }
        }

        throw new InvalidOperationException(
            $"Nenhum QCY MeloBuds N50 pareado ({(preferClassic ? "Classic" : "BLE")}) foi encontrado automaticamente. " +
            "Informe o MAC manualmente (ex.: AA:BB:CC:DD:EE:FF) ou execute 'scan' primeiro.");
    }

    private static string FormatMac(ulong addr)
    {
        Span<byte> b = stackalloc byte[6];
        for (int i = 0; i < 6; i++)
        {
            b[5 - i] = (byte)((addr >> (i * 8)) & 0xFF);
        }
        return $"{b[0]:X2}:{b[1]:X2}:{b[2]:X2}:{b[3]:X2}:{b[4]:X2}:{b[5]:X2}";
    }

    private static string Hex(ReadOnlySpan<byte> data) =>
        string.Join(' ', data.ToArray().Select(b => b.ToString("X2")));

    private static void Log(string msg) =>
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");

    private static async Task<(bool Ok, T? Value)> TryOp<T>(IAsyncOperation<T> op, int timeoutMs)
    {
        var task = op.AsTask();
        if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
        {
            try { return (true, await task); }
            catch (Exception ex)
            {
                Log($"  WinRT op falhou: {ex.GetType().Name}: {ex.Message}");
                return (false, default);
            }
        }
        Log($"  WinRT op expirou apos {timeoutMs} ms");
        return (false, default);
    }

    // =========================================================================
    // 1. SCAN (somente leitura: pareados Windows + Advertising BLE 0x521C)
    // =========================================================================
    private static async Task<int> RunScanAsync(int seconds)
    {
        Log("=== 1. Dispositivos Bluetooth pareados no Windows (QCY / N50) ===");
        string[] extraProps =
        [
            "System.Devices.Aep.DeviceAddress",
            "System.Devices.Aep.IsConnected",
            "System.Devices.Aep.IsPaired",
            "System.Devices.BatteryLife"
        ];

        await ListPairedBySelectorAsync("Classic", BluetoothDevice.GetDeviceSelectorFromPairingState(true), extraProps);
        await ListPairedBySelectorAsync("BLE", BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), extraProps);

        Log($"=== 2. Varredura BLE AdvertisementWatcher ({seconds}s, Active) ===");
        var seen = new ConcurrentDictionary<string, int>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(500);

        watcher.Received += (_, e) =>
        {
            string mac = FormatMac(e.BluetoothAddress);
            string localName = e.Advertisement.LocalName ?? "";
            bool hasQcyMfg = false;

            foreach (var mfg in e.Advertisement.ManufacturerData)
            {
                byte[] bytes = ReadBuffer(mfg.Data);
                if (mfg.CompanyId == QcyUuids.QcyCompanyId)
                {
                    hasQcyMfg = true;
                    string key = $"521C:{mac}:{Convert.ToHexString(bytes)}";
                    int count = seen.AddOrUpdate(key, 1, (_, c) => c + 1);
                    if (count == 1)
                    {
                        Log($"[ADV 0x521C] BLE={mac} AddrType={e.BluetoothAddressType} RSSI={e.RawSignalStrengthInDBm}dBm Name='{localName}' Type={e.AdvertisementType} Len={bytes.Length}");
                        Log($"             RAW: {Hex(bytes)}");
                        if (QcyAdvertisementParser.TryParse(bytes, out var parsed) && parsed is not null)
                        {
                            Log($"             VendorId=0x{parsed.VendorId:X4} ({parsed.VendorId}) IsJL={parsed.IsJieLi} IsNewJL={parsed.IsNewJieLi} IsN50={parsed.IsN50} ColorIdx={parsed.ColorIndex}");
                            Log($"             Bateria: L={FmtBat(parsed.Left)} | R={FmtBat(parsed.Right)} | Estojo={FmtBat(parsed.Case)} | IsNoBattery={parsed.IsNoBattery}");
                            Log($"             ClassMac={parsed.ClassicMac} | OtherMac={parsed.OtherMac}");
                        }
                    }
                }
            }

            if (!hasQcyMfg && (localName.Contains("QCY", StringComparison.OrdinalIgnoreCase) ||
                               localName.Contains("N50", StringComparison.OrdinalIgnoreCase)))
            {
                string key = $"NAME:{mac}:{localName}:{e.AdvertisementType}";
                if (seen.TryAdd(key, 1))
                {
                    var svcUuids = e.Advertisement.ServiceUuids.Select(u => u.ToString());
                    var mfgSummary = e.Advertisement.ManufacturerData.Select(m => $"0x{m.CompanyId:X4}({m.Data.Length}B)={Hex(ReadBuffer(m.Data))}");
                    Log($"[ADV MATCH] BLE={mac} RSSI={e.RawSignalStrengthInDBm}dBm Name='{localName}' Type={e.AdvertisementType}");
                    if (svcUuids.Any()) Log($"            Services: {string.Join(", ", svcUuids)}");
                    if (mfgSummary.Any()) Log($"            MfgData:  {string.Join(" ; ", mfgSummary)}");
                }
            }
        };

        watcher.Start();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        watcher.Stop();
        Log($"Varredura concluida. Eventos unicos relevantes: {seen.Count}.");
        return 0;
    }

    private static async Task ListPairedBySelectorAsync(string kind, string selector, string[] extraProps)
    {
        var (ok, devices) = await TryOp(DeviceInformation.FindAllAsync(selector, extraProps), 6000);
        if (!ok || devices is null) return;

        foreach (var di in devices)
        {
            if (!di.Name.Contains("QCY", StringComparison.OrdinalIgnoreCase) &&
                !di.Name.Contains("N50", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            di.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addr);
            di.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var conn);
            di.Properties.TryGetValue("System.Devices.BatteryLife", out var bat);
            Log($"  [{kind}] Name='{di.Name}' Addr={addr} Connected={conn} BatteryLife={bat ?? "(null)"} Id={di.Id}");
        }
    }

    private static string FmtBat(BatteryByte b) =>
        b.Percent is null ? "--" : $"{b.Percent}%{(b.IsCharging ? " (carregando)" : "")}";

    // =========================================================================
    // 1B. DIAG (somente leitura: diagnostica como o Windows conecta ao A002)
    // =========================================================================
    private static async Task<int> RunDiagAsync()
    {
        Log("=== DIAG 1: Capturando AddressType do anuncio 0x521C (4s) ===");
        ulong? advMac = null;
        BluetoothAddressType advAddrType = BluetoothAddressType.Public;
        ulong? classMac = null;
        ulong? otherMac = null;
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        watcher.Received += (_, e) =>
        {
            foreach (var m in e.Advertisement.ManufacturerData)
            {
                if (m.CompanyId != QcyUuids.QcyCompanyId) continue;
                byte[] b = ReadBuffer(m.Data);
                if (QcyAdvertisementParser.TryParse(b, out var parsed) && parsed is not null && parsed.IsN50)
                {
                    if (advMac is null)
                    {
                        advMac = e.BluetoothAddress;
                        advAddrType = e.BluetoothAddressType;
                        classMac = ParseMac(parsed.ClassicMac);
                        otherMac = ParseMac(parsed.OtherMac);
                        Log($"  Visto N50: BLE={FormatMac(e.BluetoothAddress)} AddrType={e.BluetoothAddressType} RSSI={e.RawSignalStrengthInDBm} Type={e.AdvertisementType} ClassMac={parsed.ClassicMac} OtherMac={parsed.OtherMac}");
                    }
                }
            }
        };
        watcher.Start();
        await Task.Delay(4000);
        watcher.Stop();

        Log("=== DIAG 2: Instancias conhecidas do servico GATT A002 no Windows ===");
        var svcSel = GattDeviceService.GetDeviceSelectorFromUuid(QcyUuids.JieLiService);
        var (okKnown, knownSvcs) = await TryOp(DeviceInformation.FindAllAsync(svcSel), 5000);
        if (okKnown && knownSvcs is not null)
        {
            foreach (var s in knownSvcs)
            {
                Log($"  Known A002: Name='{s.Name}' IsEnabled={s.IsEnabled} Id={s.Id}");
            }
        }

        Log("=== DIAG 3: Testando caminhos de abertura GATT (somente leitura) ===");
        if (advMac is not null)
        {
            await TestConnectPathAsync($"FromBluetoothAddress({FormatMac(advMac.Value)}, {advAddrType})",
                () => BluetoothLEDevice.FromBluetoothAddressAsync(advMac.Value, advAddrType));
        }

        if (classMac is not null)
        {
            await TestConnectPathAsync($"FromBluetoothAddress(ClassMac {FormatMac(classMac.Value)})",
                () => BluetoothLEDevice.FromBluetoothAddressAsync(classMac.Value));
        }

        if (okKnown && knownSvcs is not null && knownSvcs.Count > 0)
        {
            string svcId = knownSvcs[0].Id;
            Log($"--- Testando GattDeviceService.FromIdAsync({svcId}) ---");
            var (okS, svc) = await TryOp(GattDeviceService.FromIdAsync(svcId), 8000);
            if (okS && svc is not null)
            {
                try
                {
                    Log($"  Session.Status={svc.Session?.SessionStatus} MaxPdu={svc.Session?.MaxPduSize} DevStatus={svc.Device?.ConnectionStatus} Access={await svc.RequestAccessAsync()}");
                    var (okOpen, openSt) = await TryOp(svc.OpenAsync(GattSharingMode.SharedReadAndWrite), 8000);
                    Log($"  OpenAsync(SharedReadAndWrite): ok={okOpen} status={openSt}");
                    var (okC, cRes) = await TryOp(svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached), 8000);
                    Log($"  GetCharacteristics(Uncached): ok={okC} status={cRes?.Status} protoErr={cRes?.ProtocolError} count={cRes?.Characteristics.Count}");
                    if (okC && cRes?.Status == GattCommunicationStatus.Success)
                    {
                        var notifyCh = cRes.Characteristics.FirstOrDefault(c => c.Uuid == QcyUuids.JieLiNotify);
                        if (notifyCh is not null)
                        {
                            var (okN, nRes) = await TryOp(notifyCh.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify), 8000);
                            Log($"  CCCD Notify: ok={okN} status={nRes?.Status} protoErr={nRes?.ProtocolError}");
                            if (okN && nRes?.Status == GattCommunicationStatus.Success)
                            {
                                await TryOp(notifyCh.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                                    GattClientCharacteristicConfigurationDescriptorValue.None), 3000);
                            }
                        }
                    }
                }
                finally
                {
                    svc.Dispose();
                }
            }
        }

        return 0;
    }

    private static async Task TestConnectPathAsync(string label, Func<IAsyncOperation<BluetoothLEDevice>> openFn)
    {
        Log($"--- Testando {label} ---");
        var (okD, dev) = await TryOp(openFn(), 6000);
        if (!okD || dev is null)
        {
            Log("  dev = null");
            return;
        }

        try
        {
            Log($"  Name='{dev.Name}' Addr={FormatMac(dev.BluetoothAddress)} AddrType={dev.BluetoothAddressType} Status={dev.ConnectionStatus} Paired={dev.DeviceInformation?.Pairing?.IsPaired}");
            var (okS, sRes) = await TryOp(dev.GetGattServicesForUuidAsync(QcyUuids.JieLiService, BluetoothCacheMode.Uncached), 8000);
            Log($"  GetGattServicesForUuid(A002, Uncached): ok={okS} status={sRes?.Status} protoErr={sRes?.ProtocolError} count={sRes?.Services.Count}");
            if (okS && sRes?.Status == GattCommunicationStatus.Success && sRes.Services.Count > 0)
            {
                using var svc = sRes.Services[0];
                Log($"  Session.Status={svc.Session?.SessionStatus} MaxPdu={svc.Session?.MaxPduSize} DevStatus={dev.ConnectionStatus}");
                var (okC, cRes) = await TryOp(svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached), 8000);
                Log($"  GetCharacteristics(Uncached): ok={okC} status={cRes?.Status} protoErr={cRes?.ProtocolError} count={cRes?.Characteristics.Count}");
                if (okC && cRes?.Status == GattCommunicationStatus.Success)
                {
                    var notifyCh = cRes.Characteristics.FirstOrDefault(c => c.Uuid == QcyUuids.JieLiNotify);
                    if (notifyCh is not null)
                    {
                        var (okN, nRes) = await TryOp(notifyCh.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify), 8000);
                        Log($"  CCCD Notify: ok={okN} status={nRes?.Status} protoErr={nRes?.ProtocolError}");
                        if (okN && nRes?.Status == GattCommunicationStatus.Success)
                        {
                            await TryOp(notifyCh.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.None), 3000);
                        }
                    }
                }
            }
        }
        finally
        {
            dev.Dispose();
        }
    }

    // =========================================================================
    // 2. PROBE (100% SOMENTE LEITURA sobre GATT A002)
    // =========================================================================
    private static async Task<int> RunReadOnlyProbeAsync(ulong mac)
    {
        await using var link = await ProbeGattLink.ConnectAsync(mac);
        if (link is null) return 2;

        Log("=== Iniciando sondagem RCSP 100% SOMENTE LEITURA ===");

        // 1. Target Info (0x03): Firmware (5), MTU (13), Nome (16)
        await ProbeTargetInfoAsync(link, "Firmware (attr 5)", N50Commands.TargetMaskFirmware);
        await ProbeTargetInfoAsync(link, "MTU (attr 13)", N50Commands.TargetMaskMtu);
        await ProbeTargetInfoAsync(link, "Nome Target (attr 16)", N50Commands.TargetMaskDeviceName);

        // 2. ADV_GET (0xC1): Bateria, Lote do APK, Toques, Balanco, Idioma, Nome
        await ProbeAdvGetAsync(link, "Bateria (attr 0)", N50Commands.AdvMaskBattery);
        await ProbeAdvGetAsync(link, "Lote APK updateBattery (0x00000D21: bat/work/inear/sleep/noise)", N50Commands.AdvMaskAppBatteryBatch);
        await ProbeAdvGetAsync(link, "Tabela de Toques (attr 2)", N50Commands.AdvMaskTouchKeys);
        await ProbeAdvGetAsync(link, "Balanco L/R (attr 12)", N50Commands.AdvMaskBalance);
        await ProbeAdvGetAsync(link, "Idioma (attr 9)", N50Commands.AdvMaskLanguage);
        await ProbeAdvGetAsync(link, "Nome ADV (attr 1)", N50Commands.AdvMaskDeviceName);

        // 3. GET_SYS_INFO (0x07): EQ Grafico 10 bandas
        await ProbeGraphicEqAsync(link);

        // 4. CUSTOM (0xFF) leituras FE 01 <id>
        byte[] customReads =
        [
            0x17, // ANC
            0x24, // Multipoint
            0x14, // Auto-off timer
            0x1D, // Prompt volume
            0x22, // Parametric EQ v2
            0x20, // Parametric EQ v1
            0x09, // Game mode (custom)
            0x10, // Sleep mode (custom)
            0x2F, // Battery (custom)
            0x16, // Balance (custom)
            0x0C, // Legacy noise (custom)
            0x30  // Version (custom)
        ];

        foreach (byte itemId in customReads)
        {
            await ProbeCustomReadAsync(link, itemId);
        }

        Log("=== Aguardando 2.5s por notificacoes espontaneas ===");
        await Task.Delay(2500);
        Log("=== Sondagem SOMENTE LEITURA concluida com sucesso ===");
        return 0;
    }

    // =========================================================================
    // 2B. PROBE-SPP (100% SOMENTE LEITURA sobre Classic RFCOMM 00001101)
    // =========================================================================
    private static async Task<int> RunSppReadOnlyProbeAsync(ulong classicMac)
    {
        Log($"=== Conectando RFCOMM SPP (00001101) em {FormatMac(classicMac)} ===");
        var (okDev, dev) = await TryOp(BluetoothDevice.FromBluetoothAddressAsync(classicMac), 8000);
        if (!okDev || dev is null)
        {
            Log("ERRO: BluetoothDevice classico nao encontrado.");
            return 2;
        }

        using (dev)
        {
            Log($"Classic aberto: Name='{dev.Name}' Status={dev.ConnectionStatus}");
            var (okSvcs, svcs) = await TryOp(dev.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached), 8000);
            if (!okSvcs || svcs is null || svcs.Services.Count == 0)
            {
                Log($"GetRfcommServicesForId(Uncached) falhou ({svcs?.Error}), tentando Cached...");
                (okSvcs, svcs) = await TryOp(dev.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached), 6000);
            }

            if (!okSvcs || svcs is null || svcs.Services.Count == 0)
            {
                Log($"ERRO: Nenhum servico SPP encontrado ({svcs?.Error}).");
                return 2;
            }

            using var rfcomm = svcs.Services[0];
            Log($"SPP encontrado: Host={rfcomm.ConnectionHostName} Service={rfcomm.ConnectionServiceName} Protection={rfcomm.ProtectionLevel}");

            using var socket = new StreamSocket();
            using var cts = new CancellationTokenSource(8000);
            try
            {
                await socket.ConnectAsync(rfcomm.ConnectionHostName, rfcomm.ConnectionServiceName, rfcomm.ProtectionLevel)
                    .AsTask(cts.Token);
            }
            catch (Exception ex)
            {
                Log($"ERRO ao conectar RFCOMM SPP: {ex.GetType().Name}: {ex.Message}");
                return 2;
            }

            Log("RFCOMM SPP conectado! Enviando consultas RCSP SOMENTE LEITURA...");
            var framer = new RcspFramer();
            var rxQueue = new ConcurrentQueue<RcspFrame>();
            var rxSignal = new SemaphoreSlim(0);
            using var readCts = new CancellationTokenSource();

            var readerTask = Task.Run(async () =>
            {
                using var dr = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                try
                {
                    while (!readCts.IsCancellationRequested)
                    {
                        uint loaded = await dr.LoadAsync(512).AsTask(readCts.Token);
                        if (loaded == 0) break;
                        byte[] raw = new byte[loaded];
                        dr.ReadBytes(raw);
                        Log($"  [SPP RX RAW] {Hex(raw)}");
                        foreach (var f in framer.Feed(raw))
                        {
                            string dir = f.IsCommand ? "CMD(Fone->Host)" : $"RESP(status=0x{f.Status:X2})";
                            Log($"  [SPP RX FRAME] {dir} flags=0x{f.Flags:X2} op=0x{f.OpCode:X2} sn=0x{f.SequenceNumber:X2} param=[{Hex(f.ParamData)}]");
                            if (f.IsCommand && f.ExpectsResponse)
                            {
                                byte[] ack = N50Commands.BuildAck(f.OpCode, f.SequenceNumber);
                                ProtocolGuard.ValidateOutboundFrame(ack);
                                using var aw = new DataWriter(socket.OutputStream);
                                aw.WriteBytes(ack);
                                await aw.StoreAsync();
                                aw.DetachStream();
                                Log($"  [SPP AUTO-ACK] {Hex(ack)}");
                            }
                            rxQueue.Enqueue(f);
                            rxSignal.Release();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log($"  [SPP RX END] {ex.GetType().Name}: {ex.Message}"); }
            });

            using var dw = new DataWriter(socket.OutputStream);
            byte sn = 0;
            async Task<RcspFrame?> SendSppAsync(string label, byte[] frame, byte expectedOp, byte expectedSn, int waitMs = 2000)
            {
                ProtocolGuard.ValidateOutboundFrame(frame);
                while (rxQueue.TryDequeue(out _)) { }
                Log($"[SPP TX] {label}: {Hex(frame)}");
                dw.WriteBytes(frame);
                await dw.StoreAsync();
                await dw.FlushAsync();

                var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < deadline)
                {
                    int rem = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (!await rxSignal.WaitAsync(rem)) break;
                    while (rxQueue.TryDequeue(out var f))
                    {
                        if (f.IsResponse && f.OpCode == expectedOp && f.SequenceNumber == expectedSn)
                            return f;
                    }
                }
                Log($"  [SPP TIMEOUT] Sem resposta para {label}");
                return null;
            }

            byte NextSn() => ++sn == 0 ? ++sn : sn;

            byte s1 = NextSn();
            var rFw = await SendSppAsync("Firmware (0x03 attr 5)", N50Commands.BuildGetTargetInfo(s1, N50Commands.TargetMaskFirmware), RcspOpCode.GetTargetInfo, s1);
            if (rFw?.IsSuccessResponse == true)
            {
                var ti = N50Parsers.ParseTargetInfo(rFw.ParamData);
                Log($"  -> DECODIFICADO: Firmware={ti.FirmwareVersion}");
            }

            byte s1b = NextSn();
            var rMtu = await SendSppAsync("MTU+Nome Target (0x03 attr 13+16)", N50Commands.BuildGetTargetInfo(s1b, N50Commands.TargetMaskMtu | N50Commands.TargetMaskDeviceName), RcspOpCode.GetTargetInfo, s1b);
            if (rMtu?.IsSuccessResponse == true)
            {
                var ti = N50Parsers.ParseTargetInfo(rMtu.ParamData);
                Log($"  -> DECODIFICADO: SendMtu={ti.SendMtu} RecvMtu={ti.RecvMtu} Name={ti.DeviceName}");
            }

            byte s2 = NextSn();
            var rAdv = await SendSppAsync("ADV_GET batch (0x00000D21)", N50Commands.BuildAdvGet(s2, N50Commands.AdvMaskAppBatteryBatch), RcspOpCode.AdvGet, s2);
            if (rAdv?.IsSuccessResponse == true) PrintDecodedAdv(rAdv.ParamData);

            byte s2b = NextSn();
            var rLangName = await SendSppAsync("ADV_GET Nome+Idioma (attr 1+9)", N50Commands.BuildAdvGet(s2b, N50Commands.AdvMaskDeviceName | N50Commands.AdvMaskLanguage), RcspOpCode.AdvGet, s2b);
            if (rLangName?.IsSuccessResponse == true) PrintDecodedAdv(rLangName.ParamData);

            byte s3 = NextSn();
            var rKeys = await SendSppAsync("ADV_GET toques (attr 2)", N50Commands.BuildAdvGet(s3, N50Commands.AdvMaskTouchKeys), RcspOpCode.AdvGet, s3);
            if (rKeys?.IsSuccessResponse == true) PrintDecodedAdv(rKeys.ParamData);

            byte s4 = NextSn();
            var rBal = await SendSppAsync("ADV_GET balanco (attr 12)", N50Commands.BuildAdvGet(s4, N50Commands.AdvMaskBalance), RcspOpCode.AdvGet, s4);
            if (rBal?.IsSuccessResponse == true) PrintDecodedAdv(rBal.ParamData);

            byte s5 = NextSn();
            var rEq = await SendSppAsync("EQ Grafico (0x07)", N50Commands.BuildGetGraphicEq(s5), RcspOpCode.GetSysInfo, s5);
            if (rEq?.IsSuccessResponse == true)
            {
                var eq = N50Parsers.ParseGraphicEq(rEq.ParamData);
                Log(eq is not null ? $"  -> EQ Grafico: mode=0x{eq.Mode:X2} gains=[{string.Join(", ", eq.GainsDb)}]" : $"  -> EQ raw=[{Hex(rEq.ParamData)}]");
            }

            foreach (byte itemId in new byte[] { 0x17, 0x24, 0x14, 0x1D, 0x22, 0x20, 0x09, 0x10, 0x2F, 0x16, 0x0C, 0x30 })
            {
                byte sc = NextSn();
                var rc = await SendSppAsync($"Custom 0xFF read 0x{itemId:X2}", N50Commands.BuildCustomRead(sc, itemId), RcspOpCode.Custom, sc, 1500);
                if (rc?.IsSuccessResponse == true && rc.ParamData.Length > 0) PrintDecodedCustom(rc.ParamData);
            }

            readCts.Cancel();
            dw.DetachStream();
            await Task.WhenAny(readerTask, Task.Delay(500));
            return 0;
        }
    }

    private static async Task<int> RunSppSetAncAsync(string[] args)
    {
        if (args.Length < 4) { PrintUsage(); return 1; }
        byte mode = byte.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte scene = byte.Parse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte level = byte.Parse(args[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ulong mac = await ResolveMacAsync(args.ElementAtOrDefault(4), preferClassic: true);

        return await WithSppLinkAsync(mac, async (send, nextSn) =>
        {
            Log("--- ANTES ---");
            byte s0 = nextSn();
            var r0 = await send("Read ANC antes", N50Commands.BuildCustomRead(s0, 0x17), RcspOpCode.Custom, s0);
            if (r0?.IsSuccessResponse == true) PrintDecodedCustom(r0.ParamData);

            Log($"--- ENVIANDO ANC ({mode:X2} {scene:X2} {level:X2}) ---");
            byte s1 = nextSn();
            var r1 = await send($"Set ANC {mode:X2} {scene:X2} {level:X2}", N50Commands.BuildSetAncCustom(s1, mode, scene, level), RcspOpCode.Custom, s1);
            if (r1?.IsSuccessResponse == true && r1.ParamData.Length > 0) PrintDecodedCustom(r1.ParamData);

            await Task.Delay(650);
            Log("--- DEPOIS (read-back) ---");
            byte s2 = nextSn();
            var r2 = await send("Read ANC depois", N50Commands.BuildCustomRead(s2, 0x17), RcspOpCode.Custom, s2);
            if (r2?.IsSuccessResponse == true) PrintDecodedCustom(r2.ParamData);
        });
    }

    private static async Task<int> RunSppSetGameAsync(string[] args)
    {
        if (args.Length < 2) { PrintUsage(); return 1; }
        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        ulong mac = await ResolveMacAsync(args.ElementAtOrDefault(2), preferClassic: true);

        return await WithSppLinkAsync(mac, async (send, nextSn) =>
        {
            Log("--- ANTES ---");
            byte s0 = nextSn();
            var r0 = await send("Read WorkMode antes", N50Commands.BuildAdvGet(s0, N50Commands.AdvMaskWorkMode), RcspOpCode.AdvGet, s0);
            if (r0?.IsSuccessResponse == true) PrintDecodedAdv(r0.ParamData);

            Log($"--- ENVIANDO Modo Jogo ({(enable ? "ON" : "OFF")}) ---");
            byte s1 = nextSn();
            await send($"Set GameMode ADV ({enable})", N50Commands.BuildSetGameModeAdv(s1, enable), RcspOpCode.AdvSet, s1);

            await Task.Delay(400);
            Log("--- DEPOIS (read-back) ---");
            byte s2 = nextSn();
            var r2 = await send("Read WorkMode depois", N50Commands.BuildAdvGet(s2, N50Commands.AdvMaskWorkMode), RcspOpCode.AdvGet, s2);
            if (r2?.IsSuccessResponse == true) PrintDecodedAdv(r2.ParamData);
        });
    }

    private static async Task<int> RunSppWatchAsync(int seconds, ulong mac)
    {
        return await WithSppLinkAsync(mac, async (_, _) =>
        {
            Log($"Escutando RFCOMM SPP por {seconds}s (toque nos fones ou abra o app do celular para observar)...");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        });
    }

    private static async Task<int> WithSppLinkAsync(
        ulong classicMac,
        Func<Func<string, byte[], byte, byte, Task<RcspFrame?>>, Func<byte>, Task> body)
    {
        var (okDev, dev) = await TryOp(BluetoothDevice.FromBluetoothAddressAsync(classicMac), 8000);
        if (!okDev || dev is null) { Log("ERRO: BluetoothDevice classico nao encontrado."); return 2; }
        using (dev)
        {
            var (okSvcs, svcs) = await TryOp(dev.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached), 6000);
            if (!okSvcs || svcs is null || svcs.Services.Count == 0)
                (okSvcs, svcs) = await TryOp(dev.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached), 8000);
            if (!okSvcs || svcs is null || svcs.Services.Count == 0) { Log("ERRO: Servico SPP nao encontrado."); return 2; }

            using var rfcomm = svcs.Services[0];
            using var socket = new StreamSocket();
            using var cts = new CancellationTokenSource(8000);
            await socket.ConnectAsync(rfcomm.ConnectionHostName, rfcomm.ConnectionServiceName, rfcomm.ProtectionLevel).AsTask(cts.Token);

            var framer = new RcspFramer();
            var rxQueue = new ConcurrentQueue<RcspFrame>();
            var rxSignal = new SemaphoreSlim(0);
            using var readCts = new CancellationTokenSource();
            var txLock = new SemaphoreSlim(1, 1);

            var readerTask = Task.Run(async () =>
            {
                using var dr = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                try
                {
                    while (!readCts.IsCancellationRequested)
                    {
                        uint loaded = await dr.LoadAsync(512).AsTask(readCts.Token);
                        if (loaded == 0) break;
                        byte[] raw = new byte[loaded];
                        dr.ReadBytes(raw);
                        Log($"  [SPP RX RAW] {Hex(raw)}");
                        foreach (var f in framer.Feed(raw))
                        {
                            string dir = f.IsCommand ? "CMD(Fone->Host)" : $"RESP(status=0x{f.Status:X2})";
                            Log($"  [SPP RX FRAME] {dir} flags=0x{f.Flags:X2} op=0x{f.OpCode:X2} sn=0x{f.SequenceNumber:X2} param=[{Hex(f.ParamData)}]");
                            if (f.IsCommand && f.ExpectsResponse)
                            {
                                byte[] ack = N50Commands.BuildAck(f.OpCode, f.SequenceNumber);
                                ProtocolGuard.ValidateOutboundFrame(ack);
                                await txLock.WaitAsync();
                                try
                                {
                                    using var aw = new DataWriter(socket.OutputStream);
                                    aw.WriteBytes(ack);
                                    await aw.StoreAsync();
                                    aw.DetachStream();
                                }
                                finally { txLock.Release(); }
                                Log($"  [SPP AUTO-ACK] {Hex(ack)}");
                            }
                            if (f.IsCommand && f.OpCode == RcspOpCode.Custom && f.ParamData.Length > 0)
                                PrintDecodedCustom(f.ParamData);
                            rxQueue.Enqueue(f);
                            rxSignal.Release();
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Log($"  [SPP RX END] {ex.GetType().Name}: {ex.Message}"); }
            });

            using var dw = new DataWriter(socket.OutputStream);
            byte sn = 0;
            byte NextSn() => ++sn == 0 ? ++sn : sn;

            async Task<RcspFrame?> SendAsync(string label, byte[] frame, byte expectedOp, byte expectedSn)
            {
                ProtocolGuard.ValidateOutboundFrame(frame);
                while (rxQueue.TryDequeue(out _)) { }
                Log($"[SPP TX] {label}: {Hex(frame)}");
                await txLock.WaitAsync();
                try
                {
                    dw.WriteBytes(frame);
                    await dw.StoreAsync();
                    await dw.FlushAsync();
                }
                finally { txLock.Release(); }

                var deadline = DateTime.UtcNow.AddMilliseconds(2200);
                while (DateTime.UtcNow < deadline)
                {
                    int rem = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                    if (!await rxSignal.WaitAsync(rem)) break;
                    while (rxQueue.TryDequeue(out var f))
                    {
                        if (f.IsResponse && f.OpCode == expectedOp && f.SequenceNumber == expectedSn)
                            return f;
                    }
                }
                Log($"  [SPP TIMEOUT] Sem resposta para {label}");
                return null;
            }

            await body(SendAsync, NextSn);
            readCts.Cancel();
            dw.DetachStream();
            await Task.WhenAny(readerTask, Task.Delay(500));
            return 0;
        }
    }

    private static async Task ProbeTargetInfoAsync(ProbeGattLink link, string label, uint mask)
    {
        byte sn = link.NextSn();
        byte[] tx = N50Commands.BuildGetTargetInfo(sn, mask);
        var rx = await link.SendAndWaitAsync(label, tx, RcspOpCode.GetTargetInfo, sn);
        if (rx is not null && rx.IsSuccessResponse)
        {
            var info = N50Parsers.ParseTargetInfo(rx.ParamData);
            Log($"  -> DECODIFICADO: Firmware={info.FirmwareVersion ?? "-"} SendMtu={info.SendMtu?.ToString() ?? "-"} RecvMtu={info.RecvMtu?.ToString() ?? "-"} Name={info.DeviceName ?? "-"}");
        }
    }

    private static async Task ProbeAdvGetAsync(ProbeGattLink link, string label, uint mask)
    {
        byte sn = link.NextSn();
        byte[] tx = N50Commands.BuildAdvGet(sn, mask);
        var rx = await link.SendAndWaitAsync(label, tx, RcspOpCode.AdvGet, sn);
        if (rx is not null && rx.IsSuccessResponse)
        {
            PrintDecodedAdv(rx.ParamData);
        }
    }

    private static void PrintDecodedAdv(byte[] paramData)
    {
        var adv = N50Parsers.ParseAdvInfo(paramData);
        var ltvs = JieLiLtv.DecodeAll(paramData);
        Log($"  -> LTVs ({ltvs.Count}): " + string.Join(" | ", ltvs.Select(x => $"type=0x{x.Type:X2} val=[{Hex(x.Value)}]")));
        if (adv.Battery is not null)
            Log($"  -> Bateria: L={FmtBat(adv.Battery.Left)} | R={FmtBat(adv.Battery.Right)} | Estojo={FmtBat(adv.Battery.Case)}");
        if (adv.WorkMode is not null)
            Log($"  -> WorkMode (attr 5): {adv.WorkMode} ({(adv.WorkMode == 2 ? "MODO JOGO ON" : adv.WorkMode == 1 ? "Normal/OFF" : "Outro")})");
        if (adv.SleepMode is not null)
            Log($"  -> SleepMode (attr 10): {adv.SleepMode} ({(adv.SleepMode == 1 ? "ON" : "OFF")})");
        if (adv.InEarState is not null)
            Log($"  -> InEar (attr 8): {adv.InEarState}");
        if (adv.Balance is not null)
            Log($"  -> Balance (attr 12): {adv.Balance} (50 = centro)");
        if (adv.Language is not null)
            Log($"  -> Language (attr 9): '{adv.Language}'");
        if (adv.DeviceName is not null)
            Log($"  -> Name (attr 1): '{adv.DeviceName}'");
        if (adv.LegacyNoise is not null)
            Log($"  -> LegacyNoise (attr 11): mode={adv.LegacyNoise.Mode} min={adv.LegacyNoise.MinGain} max={adv.LegacyNoise.MaxGain} cur={adv.LegacyNoise.CurrentGain}");
        if (adv.TouchMappings is not null)
            Log($"  -> Toques ({adv.TouchMappings.Count}): " + string.Join(", ", adv.TouchMappings.Select(t => $"{t.SideName} {t.GestureName}={t.ActionName}(0x{t.ActionJl:X2})")));
    }

    private static async Task ProbeGraphicEqAsync(ProbeGattLink link)
    {
        byte sn = link.NextSn();
        byte[] tx = N50Commands.BuildGetGraphicEq(sn);
        var rx = await link.SendAndWaitAsync("EQ Grafico GET_SYS_INFO (0x07)", tx, RcspOpCode.GetSysInfo, sn);
        if (rx is not null && rx.IsSuccessResponse)
        {
            var eq = N50Parsers.ParseGraphicEq(rx.ParamData);
            if (eq is not null)
            {
                Log($"  -> EQ Grafico: mode=0x{eq.Mode:X2} ({eq.Mode}) gains=[{string.Join(", ", eq.GainsDb)}]");
            }
            else
            {
                Log($"  -> EQ Grafico sem attr 0x04; param=[{Hex(rx.ParamData)}]");
            }
        }
    }

    private static async Task ProbeCustomReadAsync(ProbeGattLink link, byte itemId)
    {
        byte sn = link.NextSn();
        byte[] tx = N50Commands.BuildCustomRead(sn, itemId);
        var rx = await link.SendAndWaitAsync($"Custom 0xFF read item 0x{itemId:X2}", tx, RcspOpCode.Custom, sn, waitMs: 1800);
        if (rx is not null && rx.IsSuccessResponse && rx.ParamData.Length > 0)
        {
            PrintDecodedCustom(rx.ParamData);
        }
    }

    private static void PrintDecodedCustom(byte[] paramData)
    {
        var tlvs = QcyTlv.DecodeAll(paramData);
        if (tlvs.Count == 0)
        {
            Log($"  -> Custom payload bruto (nao-TLV): [{Hex(paramData)}]");
            return;
        }

        foreach (var t in tlvs)
        {
            string extra = t.Id switch
            {
                0x17 => N50Parsers.ParseAncItem(t.Value)?.Describe() ?? "invalido",
                0x14 => N50Parsers.ParseAutoOffItem(t.Value) is { } ao
                    ? (ao.IsDisabled ? "Desligado (0xFFFF)" : $"Config={ao.ConfiguredMinutes}min Restante={ao.RemainingMinutes}min")
                    : "invalido",
                0x1D => N50Parsers.ParsePromptVolumeItem(t.Value) is { } pv
                    ? $"{pv.Volume}/{pv.MaxVolume}"
                    : "invalido",
                0x22 or 0x20 => N50Parsers.ParseParametricEqItem(t.Id, t.Value) is { } peq
                    ? $"eqIdx=0x{peq.EqIndex:X2} master={peq.MasterGainDb:F2}dB bands={peq.Bands.Count} [{string.Join(", ", peq.Bands.Select(b => $"{b.FrequencyHz}Hz:{b.GainDb:+0.0;-0.0;0}dB/Q{b.QFactor:F2}"))}]"
                    : "invalido",
                0x24 when t.Value.Length >= 1 => t.Value[0] == 1 ? "Multiponto ON" : $"Multiponto OFF (0x{t.Value[0]:X2})",
                0x09 when t.Value.Length >= 1 => t.Value[0] == 1 ? "GameMode ON" : $"GameMode OFF (0x{t.Value[0]:X2})",
                0x10 when t.Value.Length >= 1 => t.Value[0] == 1 ? "Sleep ON" : $"Sleep OFF (0x{t.Value[0]:X2})",
                0x2F when t.Value.Length >= 3 => $"L={FmtBat(BatteryByte.Decode(t.Value[0]))} R={FmtBat(BatteryByte.Decode(t.Value[1]))} Case={FmtBat(BatteryByte.Decode(t.Value[2]))}",
                _ => ""
            };
            Log($"  -> TLV id=0x{t.Id:X2} len={t.Value.Length} val=[{Hex(t.Value)}]{(string.IsNullOrEmpty(extra) ? "" : " => " + extra)}");
        }
    }

    // =========================================================================
    // 3. SET-ANC (escrita reversivel unica + read-back)
    // =========================================================================
    private static async Task<int> RunSetAncAsync(string[] args)
    {
        if (args.Length < 4)
        {
            PrintUsage();
            return 1;
        }

        byte mode = byte.Parse(args[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte scene = byte.Parse(args[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte level = byte.Parse(args[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ulong mac = await ResolveMacAsync(args.ElementAtOrDefault(4), preferClassic: false);

        await using var link = await ProbeGattLink.ConnectAsync(mac);
        if (link is null) return 2;

        Log("--- Leitura ANTES ---");
        await ProbeCustomReadAsync(link, 0x17);

        byte sn = link.NextSn();
        byte[] tx = N50Commands.BuildSetAncCustom(sn, mode, scene, level);
        Log($"--- Enviando ANC ({mode:X2} {scene:X2} {level:X2}) ---");
        await link.SendAndWaitAsync($"Set ANC {mode:X2} {scene:X2} {level:X2}", tx, RcspOpCode.Custom, sn, waitMs: 2500);

        await Task.Delay(700);
        Log("--- Leitura DEPOIS (read-back) ---");
        await ProbeCustomReadAsync(link, 0x17);
        return 0;
    }

    // =========================================================================
    // 4. SET-GAME (escrita reversivel unica + read-back)
    // =========================================================================
    private static async Task<int> RunSetGameAsync(string[] args)
    {
        if (args.Length < 2)
        {
            PrintUsage();
            return 1;
        }

        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
        bool useCustom = args.ElementAtOrDefault(2)?.Equals("custom", StringComparison.OrdinalIgnoreCase) == true;
        ulong mac = await ResolveMacAsync(args.ElementAtOrDefault(3), preferClassic: false);

        await using var link = await ProbeGattLink.ConnectAsync(mac);
        if (link is null) return 2;

        Log("--- Leitura ANTES ---");
        await ProbeAdvGetAsync(link, "WorkMode antes (ADV 5)", N50Commands.AdvMaskWorkMode);

        byte sn = link.NextSn();
        if (useCustom)
        {
            byte[] tx = N50Commands.BuildSetGameModeCustom(sn, enable);
            await link.SendAndWaitAsync($"Set GameMode Custom ({enable})", tx, RcspOpCode.Custom, sn, waitMs: 2500);
        }
        else
        {
            byte[] tx = N50Commands.BuildSetGameModeAdv(sn, enable);
            await link.SendAndWaitAsync($"Set GameMode ADV ({enable})", tx, RcspOpCode.AdvSet, sn, waitMs: 2500);
        }

        await Task.Delay(500);
        Log("--- Leitura DEPOIS (read-back) ---");
        await ProbeAdvGetAsync(link, "WorkMode depois (ADV 5)", N50Commands.AdvMaskWorkMode);
        return 0;
    }

    // =========================================================================
    // 5. WATCH (escuta passiva de notificacoes)
    // =========================================================================
    private static async Task<int> RunWatchAsync(int seconds, ulong mac)
    {
        await using var link = await ProbeGattLink.ConnectAsync(mac);
        if (link is null) return 2;

        Log($"Escutando notificacoes GATT por {seconds}s (toque nos fones ou conecte outro app para observar)...");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        return 0;
    }

    private static byte[] ReadBuffer(IBuffer buf)
    {
        byte[] bytes = new byte[buf.Length];
        DataReader.FromBuffer(buf).ReadBytes(bytes);
        return bytes;
    }

    // =========================================================================
    // Conexao GATT segura e serializada para a sonda
    // =========================================================================
    private sealed class ProbeGattLink : IAsyncDisposable
    {
        private readonly BluetoothLEDevice _device;
        private readonly GattSession? _session;
        private readonly GattDeviceService _service;
        private readonly GattCharacteristic _writeChar;
        private readonly GattCharacteristic _notifyChar;
        private readonly GattWriteOption _writeOption;
        private readonly RcspFramer _framer = new();
        private readonly SemaphoreSlim _txLock = new(1, 1);
        private readonly ConcurrentQueue<RcspFrame> _rxQueue = new();
        private readonly SemaphoreSlim _rxSignal = new(0);
        private byte _sn;

        private ProbeGattLink(
            BluetoothLEDevice device,
            GattSession? session,
            GattDeviceService service,
            GattCharacteristic writeChar,
            GattCharacteristic notifyChar,
            GattWriteOption writeOption)
        {
            _device = device;
            _session = session;
            _service = service;
            _writeChar = writeChar;
            _notifyChar = notifyChar;
            _writeOption = writeOption;
        }

        public byte NextSn() => ++_sn == 0 ? ++_sn : _sn;

        public static async Task<ProbeGattLink?> ConnectAsync(ulong mac)
        {
            Log($"Aguardando anuncio BLE vivo de {FormatMac(mac)} (ate 10s) para sincronizar janela de conexao...");
            bool sawAdv = await WarmLeCacheAsync(mac, 10);
            Log(sawAdv ? "Anuncio BLE detectado — parando scan e conectando imediatamente." : "Sem anuncio em 10s — tentando conexao direta mesmo assim.");
            await Task.Delay(150);

            var (okDev, dev) = await TryOp(BluetoothLEDevice.FromBluetoothAddressAsync(mac, BluetoothAddressType.Public), 8000);
            if (!okDev || dev is null)
            {
                Log("ERRO: Dispositivo BLE inacessivel.");
                return null;
            }

            dev.ConnectionStatusChanged += (s, _) => Log($"  [EVENTO BLE] ConnectionStatusChanged -> {s.ConnectionStatus}");
            Log($"Dispositivo aberto: Name='{dev.Name}' Addr={FormatMac(dev.BluetoothAddress)} AddrType={dev.BluetoothAddressType} Status={dev.ConnectionStatus} Paired={dev.DeviceInformation?.Pairing?.IsPaired}");

            GattDeviceService? targetSvc = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Log($"Tentativa {attempt}/3: GetGattServicesForUuidAsync(A002, Uncached)...");
                var (okSvcs, svcsRes) = await TryOp(dev.GetGattServicesForUuidAsync(QcyUuids.JieLiService, BluetoothCacheMode.Uncached), 10000);
                Log($"  Status={svcsRes?.Status} ProtoErr={svcsRes?.ProtocolError} Count={svcsRes?.Services.Count} DevStatus={dev.ConnectionStatus}");
                if (okSvcs && svcsRes is not null && svcsRes.Status == GattCommunicationStatus.Success && svcsRes.Services.Count > 0)
                {
                    targetSvc = svcsRes.Services[0];
                    for (int i = 1; i < svcsRes.Services.Count; i++) svcsRes.Services[i].Dispose();
                    break;
                }
                await Task.Delay(1200);
            }

            if (targetSvc is null)
            {
                Log($"ERRO: Nao foi possivel conectar ao servico JieLi {QcyUuids.JieLiService} (Uncached).");
                dev.Dispose();
                return null;
            }

            GattSession? session = targetSvc.Session;
            if (session is not null)
            {
                session.MaintainConnection = true;
                session.SessionStatusChanged += (s, e) => Log($"  [EVENTO SESSION] Status={e.Status} Err={e.Error} MaxPdu={s.MaxPduSize}");
                Log($"GattSession ativa: Status={session.SessionStatus} MaxPduSize={session.MaxPduSize}");
            }

            var (okChars, charsRes) = await TryOp(targetSvc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached), 8000);
            if (!okChars || charsRes is null || charsRes.Status != GattCommunicationStatus.Success)
            {
                Log($"ERRO: Nao foi possivel listar caracteristicas de A002 ({charsRes?.Status}, protoErr={charsRes?.ProtocolError}).");
                targetSvc.Dispose();
                dev.Dispose();
                return null;
            }

            GattCharacteristic? writeChar = null;
            GattCharacteristic? notifyChar = null;
            foreach (var ch in charsRes.Characteristics)
            {
                Log($"  Char: {ch.Uuid} Props=[{ch.CharacteristicProperties}]");
                if (ch.Uuid == QcyUuids.JieLiWrite) writeChar = ch;
                if (ch.Uuid == QcyUuids.JieLiNotify) notifyChar = ch;
            }

            if (writeChar is null || notifyChar is null)
            {
                Log("ERRO: Caracteristicas 00000001/00000002 nao encontradas em A002.");
                targetSvc.Dispose();
                dev.Dispose();
                return null;
            }

            var writeOpt = writeChar.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
                ? GattWriteOption.WriteWithoutResponse
                : GattWriteOption.WriteWithResponse;
            Log($"WriteOption selecionada: {writeOpt} (Props={writeChar.CharacteristicProperties})");

            var link = new ProbeGattLink(dev, session, targetSvc, writeChar, notifyChar, writeOpt);
            notifyChar.ValueChanged += link.OnValueChanged;

            var (okCccd, cccdRes) = await TryOp(
                notifyChar.WriteClientCharacteristicConfigurationDescriptorWithResultAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify),
                8000);
            if (!okCccd || cccdRes is null || cccdRes.Status != GattCommunicationStatus.Success)
            {
                Log($"ERRO: Falha ao assinar CCCD Notify em 00000002 (status={cccdRes?.Status}, protoErr={cccdRes?.ProtocolError}).");
                await link.DisposeAsync();
                return null;
            }

            Log($"CCCD Notify assinado com sucesso em 00000002! MaxPduSize={session?.MaxPduSize}");
            return link;
        }

        private static async Task<bool> WarmLeCacheAsync(ulong targetMac, int maxSeconds)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            w.Received += (_, e) =>
            {
                if (e.BluetoothAddress == targetMac) tcs.TrySetResult(true);
            };
            w.Start();
            var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(maxSeconds)));
            w.Stop();
            return done == tcs.Task;
        }

        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            byte[] raw = ReadBuffer(args.CharacteristicValue);
            Log($"  [RX RAW] {Hex(raw)}");

            List<RcspFrame> frames;
            lock (_framer)
            {
                frames = _framer.Feed(raw);
            }

            foreach (var f in frames)
            {
                string dir = f.IsCommand ? "CMD(Fone->Host)" : $"RESP(status=0x{f.Status:X2})";
                Log($"  [RX FRAME] {dir} flags=0x{f.Flags:X2} op=0x{f.OpCode:X2} sn=0x{f.SequenceNumber:X2} param=[{Hex(f.ParamData)}]");

                // Se o fone enviar comando exigindo ACK (flags=0xC0), responder imediatamente
                if (f.IsCommand && f.ExpectsResponse)
                {
                    _ = SendAckAsync(f.OpCode, f.SequenceNumber);
                }

                if (f.IsCommand && f.OpCode == RcspOpCode.AdvNotify)
                {
                    var bat = N50Parsers.ParseAdvNotifyBattery(f.ParamData);
                    if (bat is not null)
                        Log($"    -> Push ADV_NOTIFY Bateria: L={FmtBat(bat.Left)} R={FmtBat(bat.Right)} Case={FmtBat(bat.Case)}");
                }
                else if (f.IsCommand && f.OpCode == RcspOpCode.Custom && f.ParamData.Length > 0)
                {
                    PrintDecodedCustom(f.ParamData);
                }

                _rxQueue.Enqueue(f);
                _rxSignal.Release();
            }
        }

        private async Task SendAckAsync(byte opCode, byte sn)
        {
            try
            {
                byte[] ack = N50Commands.BuildAck(opCode, sn);
                await WriteRawCheckedAsync(ack, "AUTO-ACK");
            }
            catch (Exception ex)
            {
                Log($"  Falha ao enviar AUTO-ACK op=0x{opCode:X2}: {ex.Message}");
            }
        }

        public async Task<RcspFrame?> SendAndWaitAsync(string label, byte[] frame, byte expectedOp, byte expectedSn, int waitMs = 2000)
        {
            // Drena fila antiga de frames não consumidos
            while (_rxQueue.TryDequeue(out _)) { }

            Log($"[TX] {label}: {Hex(frame)}");
            bool sent = await WriteRawCheckedAsync(frame, label);
            if (!sent) return null;

            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (DateTime.UtcNow < deadline)
            {
                int remaining = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                if (!await _rxSignal.WaitAsync(remaining))
                {
                    break;
                }

                while (_rxQueue.TryDequeue(out var f))
                {
                    if (f.IsResponse && f.OpCode == expectedOp && f.SequenceNumber == expectedSn)
                    {
                        return f;
                    }
                }
            }

            Log($"  [TIMEOUT] Sem resposta para {label} (op=0x{expectedOp:X2} sn=0x{expectedSn:X2}) em {waitMs} ms");
            return null;
        }

        private async Task<bool> WriteRawCheckedAsync(byte[] frame, string label)
        {
            // Última linha de defesa obrigatória antes de tocar o hardware
            ProtocolGuard.ValidateOutboundFrame(frame);

            await _txLock.WaitAsync();
            try
            {
                using var writer = new DataWriter();
                writer.WriteBytes(frame);
                var (ok, status) = await TryOp(_writeChar.WriteValueAsync(writer.DetachBuffer(), _writeOption), 5000);
                if (!ok || status != GattCommunicationStatus.Success)
                {
                    Log($"  [TX ERRO] {label}: status={(ok ? status.ToString() : "TIMEOUT")}");
                    return false;
                }
                return true;
            }
            finally
            {
                _txLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _notifyChar.ValueChanged -= OnValueChanged;
            try
            {
                await TryOp(
                    _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None),
                    2000);
            }
            catch { }

            try
            {
                if (_session is not null)
                {
                    _session.MaintainConnection = false;
                    _session.Dispose();
                }
            }
            catch { }

            _service.Dispose();
            _device.Dispose();
            _txLock.Dispose();
            _rxSignal.Dispose();
        }
    }
}
