# Protocolo Bluetooth (RFCOMM SPP + BLE) — QCY MeloBuds N50 (HT12)

> **Status:** Protocolo **verificado em hardware real** no **QCY MeloBuds N50 (Vendor ID `0x477A`, firmware `V_0.5.2.1`)** via **Classic Bluetooth RFCOMM SPP (`00001101`)** + **Advertising BLE passivo `0x521C`**.

---

## Legenda da Coluna `Fonte`

Todo fato técnico neste documento identifica explicitamente sua(s) origem(ns):

- **`APK`** — Decompilação estática do APK oficial QCY v4.0.7 (`com.qcymall.googleearphonesetup`, via REA 6.7.0 + JADX headless 0.7.1).
- **`PairFree`** — Documentação, tabelas `data/qcy/*.json` e vetores de teste de `Omega-Destroyer/PairFree` (GPL-3.0 — **somente fatos e vetores, zero código copiado**).
- **`Quicky`** — Base de produtos `internal/product/products.json` e `docs/protocol.md` de `hui1601/Quicky` (sem licença — **somente fatos, zero código**).
- **`OpenQCY`** — Pesquisa de protocolo e capturas reais de hardware (QCY MeloBuds N70) de `GiorgioRafael/OpenQCY-Desktop` (MIT).
- **`Hardware`** — Verificado diretamente no seu QCY MeloBuds N50 físico (preenchido na Fase B; único item já observado antes da Fase B é o serviço GATT `A002` listado pelo Windows).

---

## 1. Proveniência e Metodologia

| Item | Evidência | Fonte |
|---|---|---|
| **APK analisado** | `qcy-4-0-7.apk` (análise estática local — binário não incluído) | `APK` |
| **SHA-256 do APK** | `fe671386784a064601c24dca25d6a55c8782696c3899afed0dfc35c89998648d` | `APK` |
| **Pacote / Versão** | `com.qcymall.googleearphonesetup` · v`4.0.7` (`versionCode = 720`, 19.942 classes, 5.994 recursos) | `APK` |
| **Ferramentas** | **REA 6.7.0** (`inspect_android_package`, `search_android_classes`, `inspect_android_class`, `inspect_android_method`, `trace_android_references`) sobre **JADX headless 0.7.1** (`sha256: 6e5eacf500b64292bfb73c49797c1958f6ee44646e43e868039ae7feb573ff75`) | `APK` |
| **Referências QCY cruzadas** | **PairFree** (`docs/qcy/PROTOCOL.md`, `data/qcy/*.json`), **Quicky** (`internal/product/products.json` entradas `18298`/`18300`, `docs/protocol.md`), **OpenQCY-Desktop** (`docs/protocol-research.md`, capturas reais de N70) | `PairFree`, `Quicky`, `OpenQCY` |
| **Código nativo (`.so`)** | Nenhuma `.so` é usada no canal de controle de fones. `libjl_auth.so` e `libjl_ota_auth.so` pertencem apenas aos fluxos de OTA/DFU e relógios (`com.jieli.jl_rcsp`), não ao SDK de fones (`com.jieli.jl_bt_rcsp`). | `APK` |

---

## 2. Identificação do Modelo, Chipset e Associação de Comandos

### 2.1 Classificação por Vendor ID (`Devicebind`)

O app oficial identifica o fabricante do chip pelos bits do `vendorId` (uint16 big-endian dos bytes `0..1` do *manufacturer data* BLE `0x521C`), em `com.qcymall.earphonesetup.model.Devicebind`:

| Método / Fato | Regra exata | `0x477A` (`18298`, QCY MeloBuds N50) | `0x477C` (`18300`, QCY HT12 MeloBuds N50) | Fonte | Confiança |
|---|---|---|---|---|---|
| `Devicebind.isJLDevice()` | `(vendorId & 0xF0) == 0x70 \|\| vendorId == 512` | **`true`** (`0x70`) | **`true`** (`0x70`) | `APK`, `PairFree` | Confirmado no código |
| `Devicebind.isNewJLDevice()` | `(vendorId & 0x80F0) == 0x8070` (`33008` / `32880`) | **`false`** (`0x0070`) | **`false`** (`0x0070`) | `APK`, `PairFree` | Confirmado no código |
| `Devicebind.isNotTWS()` | `(vendorId & 0x0F00) == 0x0100 \|\| == 0x0300` | **`false`** (`0x0700`) | **`false`** (`0x0700`) | `APK` | Confirmado no código |
| `Devicebind.checkBatIDS[]` (`<clinit>`) | Lista de 147 `vendorId` cuja bateria é arredondada na UI de 10 em 10% (`Math.round(v/10f)*10`) | **Presente** (último item `0x477a`) | **Ausente** | `APK` | Confirmado no código |
| Catálogo de recursos N50 (`18298` e `18300`) | Entradas idênticas: EQ 10 bandas (±8 dB, `0000000B`), ANC 5 cenas + transparência + normal, sono (`16`), jogo (`9`), multiponto (`36`), balanço, auto-off (`20`, `repeat: 65000`), encontrar fone | Idêntico | Idêntico | `Quicky`, `PairFree` | Confirmado em catálogo (validar no hardware) |

> **Consequência prática crucial:** Como `isJLDevice() == true` e `isNewJLDevice() == false`, o N50 segue o **caminho JieLi clássico** em `JLDeviceImpl`:
> - **Bateria, Modo Jogo, Modo Sono, Detecção no ouvido, Balanço, Toques, Nome (leitura) e Idioma (leitura)** usam **Atributos ADV JieLi** (`ADV_GET 0xC1` / `ADV_SET 0xC0`).
> - **ANC com cenas (`0x17`), Multiponto (`0x24`), Desligamento programado (`0x14`), Volume de aviso (`0x1D`) e EQ paramétrico (`0x22`/`0x20`, se ativo no painel)** trafegam como **Itens QCY embutidos no opcode RCSP Custom `0xFF`** (sem o cabeçalho `FF <len>` do protocolo `A001`).
> - **EQ gráfico de 10 bandas** usa **System Info JieLi** (`GET_SYS_INFO 0x07` / `SET_SYS_INFO 0x08` / `SYS_INFO_AUTO_UPDATE 0x09`).
> - **Firmware** usa **`GET_TARGET_INFO (0x03)`** atributo `5`.

> **Sobre o arredondamento de bateria (`checkBatIDS`):** Se o fone anunciar `0x477A`, o app Android da QCY chama `Math.round(bateria / 10.0f) * 10` em `Devicebind.getLeftBattery()` / `getRightBattery()` / `getBoxBattery()`. O valor bruto enviado pelo hardware é lido sem arredondamento em `get*BatterySrc()`. Na Fase B, verificaremos o Vendor ID real e a granularidade dos bytes do seu N50.

### 2.2 Como o app associa N50/HT12 + firmware à lista de recursos

Em `com.qcymall.earphonesetup.manager.controlpan.ControlpanelJSONManager.requestNewPanelJSON2()` (`APK`):
1. Procura o cache local `JSONRequestManager.getFunJsonPath(devicebind.getName() + "_" + devicebind.getVendorIdInt())`.
2. Se não existir, consulta `ControlpanelJSONManager.localJsonMap` por `vendorId` (nenhum painel dedicado para `18298`/`18300` vem embutido offline no APK 4.0.7).
3. Fallback offline: carrega `R.raw.controlpan_jl` (se `isJLDevice()`) ou `R.raw.controlpan_default`.
4. Baixa o painel definitivo do servidor QCY via `WatchHttpAPI.downOneEarphoneNew(devicebind.getVendorIdInt(), devicebind.getVarsionInfo(), ...)` — **indexado por `(vendorId, firmwareVersion)`**. Os JSONs catalogados em `Quicky/internal/product/products.json` e `PairFree/data/qcy/devices.json` refletem exatamente esse esquema de painel remoto.
5. `ControlpanelJSONManager.analyseJSON()` interpreta `layouts[]` (`type 1/10` toques, `type 2` EQ + `sys_eq`, `type 3` encontrar fone, `type 4` switches/comandos extras, `type 6/7/9/11/12` ANC, `type 8` balanço, `type 102` desligamento programado, `type 103` volume de aviso) e preenche `readCmdIndexList` com os `cmdid`s consultados ao conectar.

---

## 3. Descoberta e Bateria Passiva via Advertising BLE (`0x521C`)

- **Company ID (Manufacturer Specific Data):** `0x521C` (`21020`, `BuildConfig.CompanyID`) (`APK`, `PairFree`, `Quicky`, `OpenQCY`).
- **Tamanho mínimo:** 20 bytes (completo: 24 bytes; se `< 24`, completado com zeros) (`APK`, `PairFree`).

| Offset (bytes) | Campo | Decodificação exata no APK | Fonte | Confiança |
|---|---|---|---|---|
| `0..1` | `vendorId` | uint16 **Big-Endian**: `((b[0] & 0xFF) << 8) \| (b[1] & 0xFF)` (ex.: `47 7A` → `0x477A`, `47 7C` → `0x477C`) | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código (N70 em hardware via `OpenQCY`) |
| `3` | `colorIndex` | `(b[3] & 0x0E) >> 1` *(Atenção: `Quicky` lista `& 0x18`, o que diverge do APK — o código decompilado `Devicebind.refreshInfoFromAVData` comprova `14 & (b[3] >> 1)` = `& 0x0E`)* | `APK` (corrige `Quicky`) | Confirmado no código |
| `5` | Bateria **L** (esquerdo) | Bit 7 (`(b[5] >> 7) & 1`): carregando. Bits 0..6 (`b[5] & 0x7F`): `%` (`1..100`; `0` ou `>100` = desconhecido/ausente e força `charging = false`) | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código |
| `6` | Bateria **R** (direito) | Mesma codificação do byte 5 | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código |
| `7` | Bateria **Estojo** | Bit 7: carregando; bits 0..6: `%` (`1..100`) | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código |
| `12, 11, 13, 16, 15, 14` | `classMac` (MAC Clássico 1) | 6 bytes nessa ordem permutada (`b[12]:b[11]:b[13]:b[16]:b[15]:b[14]`) | `APK`, `Quicky`, `OpenQCY` | Confirmado no código |
| `19, 18, 20, 23, 22, 21` | `otherMac` (MAC Clássico 2) | Mesma permutação; se `00:00:00:00:00:00`, assume `classMac` | `APK`, `Quicky` | Confirmado no código |

> **Regra de seleção de anúncio (`EarphoneListManager.updateDeviceBleMac`, `APK`):** o app só atualiza o endereço BLE de controle quando `!devicebind.isNoBattery()` (pelo menos um dos fones reporta `1..100%`). Isso evita tentar abrir GATT com o estojo fechado/fones dormindo.  
> **Hierarquia de fontes de bateria (`OpenQCY` adaptado ao N50):** (1) Advertising `0x521C` (L/R/Estojo + carga, sem GATT); (2) GATT `ADV_GET 0xC1` / `ADV_NOTIFY 0xC2` (quando conectado); (3) Fallback `Custom 0xFF` item `0x2F`; (4) Fallback agregado do Windows `System.Devices.BatteryLife` (explicitamente marcado como estimativa, nunca substituindo leituras QCY reais).

---

## 4. Camada de Transporte (RFCOMM SPP + BLE GATT) e Enquadramento JieLi RCSP

### 4.1 Transportes Verificados no Windows (Fase B — Descoberta Crítica)

| Transporte | Endereço / UUID | Comportamento observado no Windows (N50 `V_0.5.2.1`) | Fonte |
|---|---|---|---|
| **Classic Bluetooth RFCOMM SPP *(Primário no Windows)*** | MAC Clássico (`QCY MeloBuds N50`), UUID `00001101-0000-1000-8000-00805F9B34FB` (`VID_05D6` JieLi) | Conecta em **~250 ms** sobre o pareamento de áudio existente (`BluetoothEncryptionAllowNullAuthentication`), RTT **3–25 ms**, fala o protocolo **JieLi RCSP `FE DC BA … EF` completo** sem conflito de bonding BLE e sem limite de 23 bytes de MTU (`SendMtu=272`, `RecvMtu=540`). | `Hardware` (Fase B) |
| **BLE Advertising Passivo `0x521C` *(Bateria sem conectar)*** | `QCY MeloBuds N50-APP` (`AddrType=Public`, `ConnectableUndirected`), Company ID `0x521C` (24 B) | Anuncia `VendorId=0x477A`, `ColorIdx=0`, `L/R/Estojo` (bit 7 = carga) e `ClassMac` exato sem precisar abrir sessão. `ScanResponse` traz nome `QCY MeloBuds N50-APP` e serviço `0xFEE8`. | `APK`, `PairFree`, `Hardware` (Fase B) |
| **BLE GATT `0000A002` *(Secundário / fallback)*** | Service `0000A002-0000-1000-8000-00805F9B34FB`<br>Write `00000001` (`WriteWithoutResponse`)<br>Notify `00000002` (`Notify`) | Quando `QCY MeloBuds N50-APP` já está pareado (`Paired=True`) no Windows, o link LE conecta e cai em **~175 ms** (chave/bonding LE incompatível no stack do Windows). Como o RFCOMM SPP `00001101` entrega 100% do protocolo RCSP sobre a conexão de áudio já ativa, o app usa **RFCOMM SPP como canal principal** + **BLE `0x521C` para bateria passiva**. | `APK`, `PairFree`, `Hardware` (Fase B) |

> **Sequência de conexão no APK (`QCYHeadsetClient.connectDevice` + `QCYConnectManager.AnonymousClass12` + `JLDeviceImpl.<init>` + `BtRcspOpImpl.handleDeviceConnectedEvent`):**
> 1. Conectar BLE (`autoConnect = false`), descobrir serviços.
> 2. Assinar notificações de `00000002` no serviço `0000A002` (CCCD = `0x01 0x00`).
> 3. Solicitar/consultar MTU (no Windows WinRT, `GattSession.MaxPduSize` negocia automaticamente; `ParseHelper` respeita `MAX_COMMUNICATION_MTU` e `MAX_RECEIVE_MTU`).
> 4. Enviar `GET_TARGET_INFO (0x03)` serializando escritas (próximo pacote apenas após término da escrita anterior).

### 4.2 Estrutura do Frame RCSP

**Fonte:** `APK` (`ParseHelper.packSendBasePacket`, `findPacketData`, `parsePacketData`, `CHexConverter`), `PairFree` (`docs/qcy/PROTOCOL.md`, `QcyJieliSession`).  
**Confiança:** Confirmado no código.

- **Checksum / CRC:** **NÃO EXISTE.** Delimitadores de início (`FE DC BA`), tamanho (`paramLen` 2 bytes BE) e fim (`EF`).
- **Endianness (ATENÇÃO À DUALIDADE):**
  - **Camada RCSP** (`paramLen`, máscaras uint32 de `0x03`/`0x07`/`0xC1`, ganhos uint16 de `NoiseMode`, MTU `0xD1`): **Big-Endian**.
  - **Payloads QCY dentro do Custom `0xFF`** (`PowerManagerDataBean` minutos uint16, `EQParamListBean` frequência/ganho/Q int16): **Little-Endian**.
- **Fragmentação / Reassemblagem:** Uma notificação BLE pode trazer parte de um frame ou vários frames concatenados (`ParseHelper.findPacketData`).

#### Layout de Comando (`type = 1`)

```text
Offset:  0  1  2    3        4        5..6 (BE)   7      8 .. (7+paramLen-1)   7+paramLen
Bytes:  FE DC BA | flags | opCode | paramLen  |  sn  |      paramData...      |    EF
```

- `flags`: bit 7 (`0x80`) = `type` (`1` comando, `0` resposta); bit 6 (`0x40`) = `hasResponse`. Valores: **`0xC0`** (comando com resposta), **`0x80`** (sem resposta).
- `paramLen` (uint16 BE) = `1 + paramData.Length` (para todo `opCode != 0x01`).
- `sn`: contador circular `0x00..0xFF`.

#### Layout de Resposta / ACK (`type = 0`)

```text
Offset:  0  1  2    3       4        5..6 (BE)    7       8      9 .. (7+paramLen-1)   7+paramLen
Bytes:  FE DC BA | 00  | opCode | paramLen  | status |  sn  |      paramData...      |    EF
```

- `flags = 0x00`, `status = 0x00` (sucesso), `sn` ecoa o comando, `paramLen = 2 + paramData.Length`.
- **ACK obrigatório a comandos vindos do fone (`flags = 0xC0`):**
  `FE DC BA 00 <opCode> 00 02 00 <sn> EF`

#### LTV (JieLi) vs TLV (QCY Custom `0xFF`)

- **JieLi (`0x03`, `0x07`, `0x08`, `0x09`, `0xC0`, `0xC1`):** LTV `[len = 1 + value.Length] [type (1B)] [value]` (`len` inclui `type`).
- **QCY Custom (`0xFF`):** TLV `[itemId (1B)] [valLen (1B)] [value]` (`valLen` NÃO inclui `itemId`, e **não** usa o prefixo `FF <totalLen>` do protocolo `A001`).

---

## 5. Mapa Completo de Comandos para o QCY MeloBuds N50

> Nos exemplos abaixo, `<sn>` está ilustrado como `01`.

### 5.1 Identificação e Firmware (`GET_TARGET_INFO` — Opcode `0x03`)

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| Ler versão de firmware (attr `5`, mask `0x00000020`, platform `0x00`) | `FE DC BA C0 03 00 06 01 00 00 00 20 00 EF` | `FE DC BA 00 03 00 06 00 01 03 05 <b0> <b1> EF` → `V_(b0>>4).(b0&F).(b1>>4).(b1&F)` | `CommandBuilder.buildGetDeviceInfoCmd`, `ParseHelper.parseTargetInfo` (case 5), `JLDeviceImpl.getVersion` | `APK`, `PairFree` | Confirmado no código |
| Ler MTU de comunicação (attr `13`, mask `0x00002000`, platform `0x00`) | `FE DC BA C0 03 00 06 01 00 00 20 00 00 EF` | LTV `type=0x0D`: `sendMtu (u16 BE)` + opcional `recvMtu (u16 BE)` | `ParseHelper.parseTargetInfo` (case 13) | `APK` | Confirmado no código |
| Ler nome do dispositivo (attr `16`, mask `0x00010000`, **somente leitura**) | `FE DC BA C0 03 00 06 01 00 01 00 00 00 EF` | LTV `type=0x10`: string UTF-8 | `ParseHelper.parseTargetInfo` (case 16) | `APK`, `PairFree` | Confirmado no código |

*(Para privacidade e segurança, solicitamos apenas a máscara mínima necessária, ex.: `0x00002020` para firmware + MTU.)*

---

### 5.2 Bateria (L / R / Estojo + Status de Carga)

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| Ler apenas bateria (`ADV_GET 0xC1`, attr `0`, mask `0x00000001`) | `FE DC BA C0 C1 00 05 01 00 00 00 01 EF` | `FE DC BA 00 C1 00 07 00 01 04 00 <L> <R> <Case> EF` | `JLDeviceImpl.updateBattery`, `BtRcspOpImpl.getAdvInfo`, `ParseHelper.parseADVInfo` (case 0) | `APK`, `PairFree` | Confirmado no código |
| Ler pacote combinado do app (`ADV_GET 0xC1`, mask `0x00000D21` = `3361`: bits 0, 5, 8, 10, 11) | `FE DC BA C0 C1 00 05 01 00 00 0D 21 EF` | Múltiplos LTVs: `00` (bateria), `05` (work mode), `08` (in-ear), `0A` (sleep), `0B` (noise mode legado) | `JLDeviceImpl.updateBattery` | `APK` | Confirmado no código |
| Push de broadcast/bateria (`ADV_NOTIFY 0xC2`, Fone → Host) | *(iniciado pelo fone)* | `paramData[14..16]` = `<L> <R> <Case>` (bit 7 = carregando, bits 0..6 = `%`) | `ParseHelper.parseNotifyADVInfo`, `BtRcspOpImpl.handleCommand` (id 194) | `APK`, `PairFree` | Confirmado no código |
| Pedido de refresh vindo do fone (`ADV_REQUEST_OPERATION 0xC4`, Fone → Host) | Host envia ACK `FE DC BA 00 C4 00 02 00 <sn> EF` e reenvia `ADV_GET` | `op = 0x00` solicita releitura ADV | `JLDeviceImpl.<init>` (id 196), `BtRcspOpImpl.handleCommand` | `APK`, `PairFree` | Confirmado no código |
| *(Fallback se firmware responder novo JieLi)* Custom `0xFF`, item `0x2F` | `FE DC BA C0 FF 00 04 01 FE 01 2F EF` | `FE DC BA 00 FF 00 07 00 01 2F 03 <L> <R> <Case> EF` | `JLDeviceImpl.updateBattery`, `BatteryDataBean` | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código (fallback) |

Decodificação de cada byte (`L`, `R`, `Case`): `isCharging = (b & 0x80) != 0`; `percent = b & 0x7F` (válido em `1..100`; `0` ou `>100` = desconectado/desconhecido).

---

### 5.3 Controle de Ruído (ANC / Transparência / Normal)

Em `QCYConnectManager.setNoiseMode` e `JLDeviceImpl.setANCSenseMode` (`APK`), o controle de ruído com cenas empacotadas (`> 0xFF`) usa **RCSP Custom `0xFF` com item QCY `0x17` (23)**, em 3 bytes `[mode, scene, level]`.

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| **Consultar ANC atual** (`Custom 0xFF`, `FE 01 17`) | `FE DC BA C0 FF 00 04 01 FE 01 17 EF` | `FE DC BA 00 FF 00 07 00 01 17 03 <mode> <scene> <level> EF` | `JLDeviceImpl.getANCSenseLeval` → `getCustomCMDInfo(23)`, `ANCSettingDataBean` | `APK`, `PairFree`, `Quicky`, `OpenQCY` (item `0x17` validado em N70) | Confirmado no código |
| **Normal (Off)** | `FE DC BA C0 FF 00 06 01 17 03 02 00 00 EF` | ACK `0xFF` + `17 03 02 00 00` | `JLDeviceImpl.setANCSenseMode(2, 0, 0)` | `APK`, `PairFree`, `Quicky` (`131072`), `OpenQCY` | Confirmado estrutura / Inferido N50 |
| **ANC — Adaptativo** | `FE DC BA C0 FF 00 06 01 17 03 01 05 00 EF` | ACK `0xFF` + `17 03 01 05 00` | `JLDeviceImpl.setANCSenseMode(1, 5, 0)` | `APK`, `PairFree`, `Quicky` (`66816`), `OpenQCY` (N70: `01 05 00`) | Inferido N50 (validar Fase B) |
| **ANC — Cena `01` (`Noisy` no N50 / `Indoor` no N70 — VER NOTA!)** (força `00..02`, padrão `02`) | `FE DC BA C0 FF 00 06 01 17 03 01 01 02 EF` | ACK `0xFF` + `17 03 01 01 02` | `JLDeviceImpl.setANCSenseMode(1, 1, 2)` | `APK`, `PairFree`, `Quicky` (`65792..65794` = Noisy) vs `OpenQCY` (N70: `01 01 02` = Indoor) | **Conflito de rótulo entre N50 e N70** (validar Fase B!) |
| **ANC — Transporte (Commuting)** (força `00..02`, padrão `02`) | `FE DC BA C0 FF 00 06 01 17 03 01 02 02 EF` | ACK `0xFF` + `17 03 01 02 02` | `JLDeviceImpl.setANCSenseMode(1, 2, 2)` | `APK`, `PairFree`, `Quicky` (`66048..66050`), `OpenQCY` (N70: `01 02 02`) | Inferido N50 (validar Fase B) |
| **ANC — Cena `03` (`Indoor` no N50 / `Noisy` no N70 — VER NOTA!)** (força `00..02`, padrão `02`) | `FE DC BA C0 FF 00 06 01 17 03 01 03 02 EF` | ACK `0xFF` + `17 03 01 03 02` | `JLDeviceImpl.setANCSenseMode(1, 3, 2)` | `APK`, `PairFree`, `Quicky` (`66304..66306` = Indoor) vs `OpenQCY` (N70: `01 03 02` = Noisy) | **Conflito de rótulo entre N50 e N70** (validar Fase B!) |
| **ANC — Anti-vento** | `FE DC BA C0 FF 00 06 01 17 03 01 04 00 EF` | ACK `0xFF` + `17 03 01 04 00` | `JLDeviceImpl.setANCSenseMode(1, 4, 0)` | `APK`, `PairFree`, `Quicky` (`66560`), `OpenQCY` (N70: `01 04 00`) | Inferido N50 (validar Fase B) |
| **Transparência** (padrão N50 `03 01 04`; testar se níveis `00..06` funcionam) | `FE DC BA C0 FF 00 06 01 17 03 03 01 04 EF` | ACK `0xFF` + `17 03 03 01 04` | `JLDeviceImpl.setANCSenseMode(3, 1, 4)` | `APK`, `PairFree`, `Quicky` (`196868` único) | Inferido N50 (validar Fase B) |
| *(Regra especial do APK)* Transparência nível `0` (`03 01 00`) | App converte `(3, 1, 0)` → `17 03 03 02 00` | `Devicebind.getCurrentANCModeIndex` mapeia `(3, 2, *)` de volta para `0x030100` | `QCYConnectManager.setNoiseMode`, `Devicebind.getCurrentANCModeIndex` | `APK`, `Quicky` | Confirmado no código |
| *(Alternativa JieLi legada, se o N50 usar ADV attr 11)* | Leitura: `ADV_GET 0xC1` mask `0x00000800`<br>Escrita: `ADV_SET 0xC0` LTV `09 0B <mode> 06 <min2B> <max2B> <cur2B>` | `NoiseMode.parseData` (`mode`: `1`=ANC, `2`=Off, `3`=Transparência; ganhos uint16 BE) | `BtRcspOpImpl.getCurrentNoiseMode` / `setCurrentNoiseMode`, `NoiseMode.toData` | `APK`, `PairFree` | Confirmado no código (sondar leitura Fase B) |

> **Observações críticas de controle (`APK` + `OpenQCY`):**
> - **Conflito de rótulo `01 01 xx` vs `01 03 xx`:** O catálogo de `Quicky` e `PairFree` para o N50 (`18298`/`18300`) associa `01 01 xx` a *Noisy* e `01 03 xx` a *Indoor*, enquanto o `OpenQCY-Desktop` validou em hardware no N70 o inverso (`01 01 02` = *Indoor*, `01 03 02` = *Noisy*). Na Fase B, validaremos diretamente com o app oficial no celular qual rótulo corresponde a cada cena no seu N50.
> - **Debounce e fila "latest-selection-wins":** O `APK` impõe 500 ms entre escritas e ignora notificações `0x17` por 1000 ms; o `OpenQCY` confirma todos os 3 bytes da resposta e usa fila *latest-selection-wins* para que uma resposta atrasada nunca reverta um clique mais recente do usuário. Adotaremos esse padrão na Fase C.

---

### 5.4 Modo Jogo (Baixa Latência — 68 ms)

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| **Consultar Modo Jogo** (`ADV_GET 0xC1`, attr `5` `WORK_MODE`, mask `0x00000020`) | `FE DC BA C0 C1 00 05 01 00 00 00 20 EF` | `FE DC BA 00 C1 00 05 00 01 02 05 <val> EF` (`01` = Normal, `02` = Game Mode) | `JLDeviceImpl.updateBattery` / `updateSetting` (`workModel == 2`), `ParseHelper.parseADVInfo` (case 5) | `APK`, `PairFree` | Confirmado no código |
| **Ligar Modo Jogo** (`ADV_SET 0xC0`, attr `5 = 0x02`) | `FE DC BA C0 C0 00 04 01 02 05 02 EF` | `FE DC BA 00 C0 00 03 00 01 00 EF` | `JLDeviceImpl.setGameMode(true)` → `modifyAdvInfo(5, [2])` | `APK`, `PairFree` | Confirmado no código |
| **Desligar Modo Jogo** (`ADV_SET 0xC0`, attr `5 = 0x01`) | `FE DC BA C0 C0 00 04 01 02 05 01 EF` | `FE DC BA 00 C0 00 03 00 01 00 EF` | `JLDeviceImpl.setGameMode(false)` → `modifyAdvInfo(5, [1])` | `APK`, `PairFree` | Confirmado no código |
| *(Sondagem alternativa Fase B)* Custom `0xFF`, item `0x09` | Leitura: `FE DC BA C0 FF 00 04 01 FE 01 09 EF` | `09 01 01` (ON) / `09 01 02` (OFF) | `JLDeviceImpl.getGameMode` / `setGameMode` (`isNewJLDevice`), `DiyanshiDataBean` | `APK`, `PairFree`, `Quicky`, `OpenQCY` (N70: `09 01 02`) | Confirmado no código (testar leitura Fase B) |

> **Bug encontrado no APK oficial (`JLDeviceImpl.getGameMode`):** no branch `!isNewJLDevice`, o app pede `getAdvInfo(32)` (bit 5 = `WORK_MODE`), mas no callback chama erroneamente `onSleepChange(..., response.getSleepMode() == 1)`. Já `updateBattery()` e `updateSetting()` leem `response.getWorkModel() == 2` corretamente. Nosso cliente usará `workModel == 2`.

---

### 5.5 Equalizador (EQ — 10 bandas, ±8 dB no N50)

O APK 4.0.7 possui **dois caminhos de EQ** para dispositivos JieLi (`QCYConnectManager.readEQData` e `setEQMarketData`), escolhidos conforme `SysEQSeted.isSupportMultiEQ()`. O catálogo do `Quicky` lista `characteristic: 0000000B` para `18298`/`18300` (o que no esquema do painel QCY aponta para EQ gráfico de 10 bandas), mas em hardware real o QCY H3 Pro (`0x4177`, também `!isNewJLDevice`) e o N70 (`OpenQCY`) responderam ao EQ paramétrico `0x22`. Na Fase B, faremos **leitura somente** em ambos para confirmar qual o firmware do seu N50 responde.

#### Caminho 1 — EQ Gráfico JieLi via System Info (`0x07` / `0x08` / `0x09`) *(padrão para JieLi clássico)*

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| **Ler EQ** (`GET_SYS_INFO 0x07`, `func=0xFF`, mask `0x00000010` = attr `4`) | `FE DC BA C0 07 00 06 01 FF 00 00 00 10 EF` | `FE DC BA 00 07 00 10 00 01 FF 0C 04 <mode> <g0..g9> EF` (`g0..g9`: 10 `sbyte` em dB inteiro) | `JLDeviceImpl.getEQInfo`, `CommandBuilder.buildGetEqValueCmd`, `ParseHelper.convertToEqInfo` | `APK`, `PairFree`, `Quicky` (dica `0000000B`) | Confirmado no código |
| **Escrever EQ Custom** (`SET_SYS_INFO 0x08`, `mode = 0xFF`, 10 ganhos `sbyte`) | `FE DC BA C0 08 00 0F 01 FF 0C 04 FF <g0> ... <g9> EF` | `FE DC BA 00 08 00 02 00 01 EF` + possível push `0x09` | `JLDeviceImpl.setEQ`, `BtRcspOpImpl.configEqInfo` | `APK`, `PairFree` | Confirmado no código |
| **Selecionar Preset Interno** (`SET_SYS_INFO 0x08`, `mode = id`, ganhos = `0x7F` × 10) | `FE DC BA C0 08 00 0F 01 FF 0C 04 <presetId> 7F × 10 EF` | `FE DC BA 00 08 00 02 00 01 EF` | `JLDeviceImpl.setEQ` (`b != -1` preenche 10 × `0x7F`) | `APK`, `PairFree` | Confirmado no código |

- **Bandas do N50 (`PairFree`, `Quicky`):** 10 bandas, ±8 dB: `32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 Hz`.
- **Presets declarados para o N50 (`PairFree`, `Quicky`):** `Default`, `Pop`, `Heavy Bass`, `Rock`, `Soft`, `Classic` *(IDs/curvas exatas a validar via leitura de EQ na Fase B)*.

#### Caminho 2 — EQ Paramétrico QCY via Custom `0xFF` (`0x22` ou `0x20`) *(se o firmware responder `isSupportMultiEQ`)*

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| **Ler EQ Paramétrico v2** (`Custom 0xFF`, `FE 01 22`) | `FE DC BA C0 FF 00 04 01 FE 01 22 EF` | `22 <len> <eqIdx> <masterGain_s16_LE> [freq_u16_LE, gain_s16_LE, Q_u16_LE, filterType_1B] × N` | `JLDeviceImpl.getEQInfoFQG`, `EQParamListBean.setReceiveDataNew` | `APK`, `PairFree` (H3 Pro), `Quicky`, `OpenQCY` (N70) | Confirmado no código |
| **Ler EQ Paramétrico v1** (`Custom 0xFF`, `FE 01 20`) | `FE DC BA C0 FF 00 04 01 FE 01 20 EF` | `20 <len> <eqIdx> <masterGain_s16_LE> [freq_u16_LE, gain_s16_LE, Q_u16_LE] × N` (6 B/banda) | `JLDeviceImpl.getEQInfoFQG`, `EQParamListBean.setReceiveData` | `APK`, `Quicky` | Confirmado no código |
| **Escrever EQ Paramétrico v2** (`Custom 0xFF`, item `0x22`, `eqIdx = 0xFF` custom) | `FE DC BA C0 FF <len_BE> 01 22 <3+7N> FF <master_LE> <bandas... > EF` | ACK `0xFF` (`status = 0`) | `JLDeviceImpl.setEQWtihQFG2` | `APK`, `PairFree`, `Quicky`, `OpenQCY` | Confirmado no código |

- **Quantização (`APK`, `Quicky`, `OpenQCY`):** `masterGain`, `gain` (clamp `±1270` = `±12.70 dB`) e `Q` em **centésimos (`× 100`)**, **int16/uint16 Little-Endian**; `frequency` em **Hz** (uint16 Little-Endian); `filterType` (`0xFF` / `0x00` peaking).

---

### 5.6 Mapeamento de Toques (Sensor Capacitivo da Haste)

| Operação | Frame TX (hex) | Resposta RX esperada (hex) | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|
| **Ler tabela de toques** (`ADV_GET 0xC1`, attr `2`, mask `0x00000004`) | `FE DC BA C0 C1 00 05 01 00 00 00 04 EF` | LTV `type=0x02`: triplas `[side, gesture, action]` | `JLDeviceImpl.updateSetting`, `ParseHelper.parseADVInfo` (case 2) | `APK`, `PairFree` | Confirmado no código |
| **Alterar um gesto** (`ADV_SET 0xC0`, attr `2`, `[side, gesture, action_JL]`) | Ex. 2x Direito → Próxima: `FE DC BA C0 C0 00 06 01 04 02 02 02 03 EF`<br>Ex. 1x Esquerdo → Nenhuma: `FE DC BA C0 C0 00 06 01 04 02 01 01 7F EF` | `FE DC BA 00 C0 00 03 00 01 00 EF` | `JLDeviceImpl.setLeftClick*` / `setRightClick*`, `changeQCY2JL` (`0 → 0x7F`) | `APK`, `PairFree` | Confirmado no código |

- **`side`:** `0x01` = Esquerdo (L), `0x02` = Direito (R) (`APK`, `PairFree`).
- **`gesture`:** `0x01` = Simples, `0x02` = Duplo, `0x03` = Triplo, `0x04` = Quádruplo, `0x05` = Manter pressionado (*hold*) (`APK`, `PairFree`).
- **`action_JL` (`APK`, `PairFree`, `Quicky`):** `0x7F` = Nenhuma, `0x01` = Play/Pause, `0x02` = Anterior, `0x03` = Próxima, `0x04` = Assistente de voz, `0x05` = Volume +, `0x06` = Volume −, `0x07` = Modo Jogo, `0x0A` (ou `0x0B`) = Alternar ANC *(confirmar na Fase B lendo o valor de fábrica do gesto hold no seu N50!)*.

---

### 5.7 Demais Recursos (Multiponto, Modo Sono, Balanço, Auto-Off, In-Ear, Prompt Volume, Idioma, Encontrar Fone)

| Recurso | Operação | Frame TX (hex) | Resposta RX esperada | Origem no APK | Fonte | Confiança |
|---|---|---|---|---|---|---|
| **Multiponto (2 dispositivos)** | Ler / Escrever (`Custom 0xFF`, item `0x24`) | Ler: `FE DC BA C0 FF 00 04 01 FE 01 24 EF`<br>Ligar: `... 24 01 01 EF`<br>Desligar: `... 24 01 02 EF` | `24 01 01` (ON) / `24 01 02` ou `00` (OFF) | `QCYConnectManager.requestCMDData(mac, 36)` → `JLDeviceImpl.sendCustomCMD` | `APK`, `PairFree`, `Quicky` (`cmdid: 36`), `OpenQCY` (N70: `24 01 01`) | Confirmado no código |
| **Modo Sono** (desativa toques) | Ler / Escrever (`ADV_GET`/`ADV_SET`, attr `10 = 0x0A`) | Ler: `FE DC BA C0 C1 00 05 01 00 00 04 00 EF`<br>Ligar: `... 02 0A 01 EF`<br>Desligar: `... 02 0A 02 EF` | LTV `02 0A <01\|02>` | `JLDeviceImpl.getSleepMode` / `setSleepMode`, `ParseHelper.parseADVInfo` (case 10) | `APK`, `PairFree`, `Quicky` (`cmdid: 16` no `A001`), `OpenQCY` (N70: `10 01 00`) | Confirmado no código |
| **Balanço L/R** (`0..100`, centro `50 = 0x32`) | Ler / Escrever (`ADV_GET`/`ADV_SET`, attr `12 = 0x0C`) | Ler: `FE DC BA C0 C1 00 05 01 00 00 10 00 EF`<br>Centro (50): `... 02 0C 32 EF` | LTV `02 0C <00..64>` | `BtRcspOpImpl.getVolumeBalance` / `setVolumeBalance` (`!isNewJLDevice`) | `APK`, `PairFree`, `Quicky` (`channel_balance: true`) | Confirmado no código |
| **Desligamento programado** *(nunca enviar `0`!)* | Ler / Escrever (`Custom 0xFF`, item `0x14`) | Ler: `FE DC BA C0 FF 00 04 01 FE 01 14 EF`<br>30 min: `... 14 04 1E 00 00 00 EF`<br>Desligar: `... 14 04 FF FF 00 00 EF` | `14 04 <set_u16_LE> <rem_u16_LE>` (`FFFF` = desligado) | `QCYConnectManager.setPowerManager`, `PowerManagerDataBean` | `APK`, `PairFree`, `Quicky` (`cmdid: 20`, `repeat: 65000`), `OpenQCY` (N70: `14 04 FF FF 00 00`) | Confirmado no código |
| **Detecção no ouvido** *(N50 não lista oficialmente)* | Ler (`ADV_GET`, attr `8`, mask `0x00000100`) | `FE DC BA C0 C1 00 05 01 00 00 01 00 EF` | LTV `02 08 <01\|02>` (se suportado) | `JLDeviceImpl.getCheckSwitch` / `setInEarCheck` | `APK`, `PairFree`, `OpenQCY` (N70 usa `0x2C`) | Confirmado no código (suporte N50 improvável) |
| **Volume de aviso** *(sondar leitura na Fase B)* | Ler / Escrever (`Custom 0xFF`, item `0x1D`) | Ler: `FE DC BA C0 FF 00 04 01 FE 01 1D EF`<br>Escrever: `... 1D 02 <vol> 00 EF` | `1D 02 <vol> <maxVol>` (ou `1D 01 <vol>`) | `ToneVolumeManagerActivity`, `ToneVolumeDataBean` | `APK`, `Quicky`, `OpenQCY` (N70: `1D 02 04 0F`) | Confirmado no código |
| **Idioma de voz** *(somente leitura)* | Ler (`ADV_GET`, attr `9`, mask `0x00000200`) | `FE DC BA C0 C1 00 05 01 00 00 02 00 EF` | LTV `type=0x09`: string ASCII/UTF-8 | `ParseHelper.parseADVInfo` (case 9), `QCYConnectManager.readLanuage` | `APK`, `PairFree` | Confirmado no código |
| **Nome do fone** *(somente leitura!)* | Ler (`ADV_GET`, attr `1`, mask `0x00000002`) | `FE DC BA C0 C1 00 05 01 00 00 00 02 EF` | LTV `type=0x01`: string UTF-8 | `ParseHelper.parseADVInfo` (case 1) | `APK`, `PairFree` | Confirmado no código |
| **"Encontrar fone"** | **Áudio A2DP local (NÃO usa BLE!)** | *(Sem comando BLE)* | Toca alarme (`R.raw.didi2`) no stream A2DP dos fones | `FindEarbudsItemView` → `LocalMapActivity.playAlarm()`; `SearchDevCmd` tem 0 chamadas em fones | `APK`, `Quicky` (`find_earphone: true`) | Confirmado no código |

---

## 6. Divergências e Achados no Cruzamento das Fontes (`APK` × `PairFree` × `Quicky` × `OpenQCY`)

1. **Inversão de cenas ANC `01 01 xx` vs `01 03 xx` (`Quicky`/`PairFree` vs `OpenQCY`):**
   - `Quicky` e `PairFree` registram para o N50 (`18298`/`18300`): `01 01 xx` = **Noisy** e `01 03 xx` = **Indoor**.
   - `OpenQCY-Desktop` validou em hardware no N70 (`A001`) o oposto: `01 01 02` = **Indoor** e `01 03 02` = **Noisy**.
   - **Ação na Fase B:** confirmar no hardware do N50 comparando com o app QCY do celular antes de fixar os nomes das cenas na UI.
2. **Bug de máscara `colorIndex` no `Quicky` (`docs/protocol.md`):**
   - `Quicky` documenta `(data[3] & 0x18) >> 1`. O código real do `APK` (`Devicebind.refreshInfoFromAVData`) usa `14 & (bArr[3] >> 1)` = **`(b[3] & 0x0E) >> 1`**. Seguimos o `APK`.
3. **Escopo do `Quicky`:**
   - `Quicky/docs/protocol.md` documenta apenas o protocolo QCY próprio (`0000A001` / `1001` / `1002`), deixando `JLDeviceImpl` (JieLi `0000A002`) sem especificação. Contudo, seu `products.json` confirmou integralmente que `18298` (`477A`) e `18300` (`477C`) compartilham exatamente a mesma matriz de recursos e revelou `auto_off_timer.repeat = 65000` (polling de contagem regressiva a cada 65 s).
4. **Balanço L/R (`APK` vs `PairFree`):**
   - `PairFree` afirma que *todos* os JieLi usam `ADV attr 12`. No `APK`, `JLDeviceImpl` ramifica em `isNewJLDevice`: novos JieLi usam `Custom 0x16` (`22`), enquanto `!isNewJLDevice` (caso do N50) usa **`ADV attr 12`**.
5. **Bug no APK oficial em `JLDeviceImpl.getGameMode()`:**
   - Para `!isNewJLDevice`, consulta `ADV attr 5` (`WORK_MODE`), mas no callback chama `onSleepChange(response.getSleepMode() == 1)` por engano. `updateBattery()`/`updateSetting()` leem `workModel == 2` corretamente — nosso código usará `workModel == 2`.
6. **Arredondamento da bateria de `0x477A` (`Devicebind.checkBatIDS`, `APK`):**
   - `0x477A` é arredondado para múltiplos de 10% no app QCY Android; `0x477C` não. Nosso controlador exibirá o valor bruto real reportado pelo hardware.
7. **"Encontrar fone" não é comando BLE (`APK`):**
   - Confirmado via `trace_android_references` em `SearchDevCmd`: o app toca áudio via A2DP. No Windows, usaremos áudio A2DP direcionado ao endpoint N50 com opção de **pan L/R**.
8. **EQ paramétrico (`0x22`) vs EQ gráfico JieLi (`0x07`/`0x08`):**
   - `Quicky` indica `characteristic: 0000000B` (gráfico 10 bandas), mas o H3 Pro (`PairFree`, também `!isNewJLDevice`) e o N70 (`OpenQCY`) responderam a `0x22`. Na Fase B sondaremos ambos em modo **somente leitura**.

---

## 7. Lista Negra de Segurança (Comandos ESTRITAMENTE PROIBIDOS)

A biblioteca `QcyN50.Protocol` implementa uma barreira explícita (`ProtocolGuard` / allowlist estrita) que **rejeita em tempo de execução** qualquer tentativa de envio dos itens abaixo (nenhum caminho de código os constrói):

| Camada | Código proibido | Motivo | Fonte |
|---|---|---|---|
| **RCSP Opcodes** | `0x0E` (`FUNCTION`), `0xC5` (`ADV_SYSTEM_OPERATION`: reset, restore, clear pairing), `0xE7` (`REBOOT`), `0xD4`/`0xE1..0xE8` (OTA/DFU) | Destrutivo / risco de brick ou despareamento | `APK`, `PairFree` |
| **RCSP `SET_SYS_INFO (0x08)`** | Qualquer função `!= 0xFF` ou atributo `!= 0x04` (EQ) | Permite apenas escrita de EQ público | `APK` |
| **RCSP `ADV_SET (0xC0)`** | Atributo `1` (`DEVICE_NAME`), `3` (`LED`), `4` (`MIC`), `6` (`PRODUCT`), `9` (`LANGUAGE`), ou qualquer atributo fora de `{2, 5, 8, 10, 11, 12}` | Proibida troca de nome e escrita não mapeada | `APK` |
| **QCY Custom (`0xFF`)** | `0x01` (RESET_DEFAULT), `0x02` (CLEAR_PAIR), `0x03` (FACTORY_RESET), `0x0D` (TEST_MODE), `0x18` (PAIR_NAME), `0x1E` (TAKE_PHOTO), `0x1F` (STANDBY), `0x25` (RESTART), `0x26` (RESET_KEYS), `0x34` (TWS_ENABLE), `0x3E` (SYNC_TIME), `0x3F` (ALARM) | Proibidos por especificação | `APK`, `PairFree`, `Quicky` |
| **QCY Custom `0x14` (Timer)** | `minutes == 0` (`14 04 00 00 00 00`) | Desliga os fones imediatamente | `APK`, `PairFree` |
| **Qualquer opcode/item não mapeado** | Bloqueado por padrão (*default-deny*) | Nenhuma escrita cega ao hardware | Política do projeto |

---

## 8. Matriz de Verificação (Preenchida na Fase B — Hardware Real `0x477A`, `V_0.5.2.1`)

| Item | Status Fase A (Estático) | Status Fase B (Hardware real) | Evidência capturada (hex / resultado) |
|---|---|---|---|
| **Advertising `0x521C`** (Vendor ID, bateria, MACs) | Confirmado no código | **CONFIRMADO (`Hardware`)** | `47 7A 1E A0 6E 64 64 00 ...` → `VendorId=0x477A` (`18298`), `ColorIdx=0`, `L=100%`, `R=100%`, `Estojo=--` (fora do estojo fechado), `ClassMac` idêntico ao MAC Clássico pareado |
| **Transporte RCSP no Windows** | GATT `A002` previsto | **RFCOMM SPP `00001101` CONFIRMADO (`Hardware`)** | RFCOMM SPP no MAC Clássico responde 100% do protocolo RCSP em 3–25 ms. GATT `A002` (`0001=WriteWithoutResponse`, `0002=Notify`) cai em ~175 ms enquanto `…-APP` está pareado no Windows |
| **`0x03` Target Info** (Firmware + MTU) | Confirmado no código | **CONFIRMADO (`Hardware`)** | Firmware: `03 05 05 21` → **`V_0.5.2.1`**; MTU: `05 0D 01 10 02 1C` → **`SendMtu=272`, `RecvMtu=540`** |
| **`0xC1` ADV Get** (Bateria, Nome, Toques, WorkMode, Sleep, Balance) | Confirmado no código | **CONFIRMADO (`Hardware`)** | Lote `0x00000D21` → `Bateria=100/100/--`, `WorkMode=1`, `Sleep=2` (In-Ear `8` e LegacyNoise `11` ausentes); `Name` (`attr 1`) = `"QCY MeloBuds N50"`; `Balance` (`attr 12`) = `50` (`0x32`); `Toques` (`attr 2`) = 20 triplas (10 binaural + 10 modo 1 fone `0x81..0x85`; **`R Hold = 0x0B` = Alternar ANC**); `Language` (`attr 9`) ausente |
| **`0xFF` Custom Read** (`17`, `24`, `14`, `1D`, `22`) | Confirmado no código | **CONFIRMADO (`Hardware`)** | `17`: `02 01 00` (Normal); `24`: `01` (Multiponto ON); `14`: `FF FF 00 00` (Auto-Off desativado); `1D`: `08 08` (PromptVol `8/8`); **`22`: `eqIdx=0x03`, `master=-7.50 dB`, 4 bandas paramétricas** (`35/50/230/5200 Hz`). Itens `20`, `09`, `10`, `2F`, `16`, `0C`, `30` não respondem (timeout) |
| **`0x07` Sys Info Read** (EQ gráfico 10 bandas) | Confirmado no código | **NÃO SUPORTADO no `V_0.5.2.1` (`Hardware`)** | Respondeu vazio `FE DC BA 00 07 00 03 00 05 FF EF` — confirmado que o N50 usa **EQ Paramétrico v2 (`Custom 0x22`)** |
| **Escrita reversível ANC (`Custom 0x17`)** | Estrutura confirmada / valores inferidos | **CONFIRMADO (`Hardware`)** | Todas as transições confirmadas com ACK + read-back: `01 05 00` (Adaptativo), `01 01 02` (Cena 01), `01 02 02` (Transporte), `01 03 02` (Cena 03), `01 04 00` (Anti-vento), `03 01 04` e `03 01 02` (Transparência ajustável!), `02 00 00` (Normal restaurado). **Descoberta crítica:** firmware ignora silenciosamente trocas de ANC com intervalo `< ~1.5 s` — espaçamento mínimo de 1,5 s + fila *latest-selection-wins* é obrigatório. Push espontâneo `0x32` recebido e respondido com AUTO-ACK |
| **Escrita reversível Modo Jogo (`ADV_SET 5`)** | Confirmado no código | **CONFIRMADO (`Hardware`)** | ON (`02 05 02`, 11 ms) → read-back `02`; OFF (`02 05 01`, 5 ms) → read-back `01` (restaurado) |
| **Rótulos Cena `01` vs Cena `03` (`Noisy` vs `Indoor`)** | Conflito `Quicky` vs `OpenQCY` | **Pendente confirmação visual no app celular** | Ambas aceitas no hardware (`01 01 02` e `01 03 02`); validar rótulo exato ao abrir o app QCY no celular |
| **Concorrência com app QCY no celular** | Pendente | **Pendente** | App do celular estava desativado na Fase B; testar em paralelo quando o usuário quiser (ou na Fase C) |
| **Bateria do estojo (`0x521C` byte 7 / `ADV 0` byte 2)** | Confirmado no código | **Parcial (`0x00` fora do estojo)** | Como os fones estavam fora do estojo fechado, veio `0x00` (`--`); testar com tampa aberta / 1 fone no estojo na Fase C |
