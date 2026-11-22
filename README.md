# QCY MeloBuds N50 (HT12) — Documentação de Protocolo e Biblioteca de Leitura/Controle

Documentação técnica **verificada em hardware real** (`Vendor ID 0x477A`, firmware `V_0.5.2.1`) e implementação *clean-room* em C# (.NET 8, licença **MIT**) explicando **como os dados do QCY MeloBuds N50 são lidos e como enviar comandos seguros** sem depender do aplicativo oficial Android.

> **Escopo deste repositório:** apenas a especificação do protocolo, os parsers/builders puros em `src/QcyN50.Protocol`, a suíte de 48 testes unitários (incluindo capturas reais de hardware) e a ferramenta CLI `tools/N50.Probe` para leitura e validação no Windows. Nenhum app de interface ou widget é incluído aqui.

---

## Como os dados do MeloBuds N50 são lidos (Visão Rápida)

O QCY MeloBuds N50 usa chipset **JieLi** (`Vendor ID 0x477A` ou `0x477C`, caminho `isJLDevice == true` / `isNewJLDevice == false`) e expõe dados por **dois canais complementares**:

| Canal | Endereço / UUID | O que lê / faz | Descoberta prática no Windows |
|---|---|---|---|
| **1. BLE Advertising Passivo (`0x521C`)** | Anúncio `QCY MeloBuds N50-APP`, Company ID `0x521C` (24 bytes) | **Bateria L / R / Estojo + status de carga** + `Vendor ID` + `ClassMac` — **sem abrir conexão** | Funciona mesmo sem conectar GATT/RFCOMM; ideal para monitorar bateria em segundo plano. |
| **2. Classic Bluetooth RFCOMM SPP *(Primário no Windows)*** | MAC Clássico (`QCY MeloBuds N50`), UUID `00001101-0000-1000-8000-00805F9B34FB` | **Protocolo JieLi RCSP completo** (`FE DC BA … EF`): firmware, bateria, ANC (5 cenas + transparência + normal), modo jogo, sono, multiponto, balanço, auto-off, volume de avisos, toques e EQ paramétrico v2 | Conecta em **~250 ms** sobre o pareamento A2DP existente, com **RTT de 3–25 ms**, sem limite de 23 B de MTU (`SendMtu=272`, `RecvMtu=540`). |
| **3. BLE GATT `0000A002` *(Secundário)*** | Service `0000A002-0000-1000-8000-00805F9B34FB`<br>Write `00000001` (`WriteWithoutResponse`)<br>Notify `00000002` (`Notify`) | Mesmo protocolo JieLi RCSP | No Windows, se `QCY MeloBuds N50-APP` estiver pareado (`Paired=True`), o link LE cai em ~175 ms por conflito de bonding; por isso **RFCOMM SPP `00001101` é o transporte recomendado no PC**. |

---

## 1. Leitura de Bateria sem Conectar (BLE Manufacturer Data `0x521C`)

Pacote de **20 a 24 bytes** anunciado no *Manufacturer Specific Data* com Company ID `0x521C` (`21020`):

| Offset | Campo | Decodificação |
|---|---|---|
| `0..1` | `vendorId` | `uint16` **Big-Endian** (`47 7A` → `0x477A`, `47 7C` → `0x477C`) |
| `3` | `colorIndex` | `(b[3] & 0x0E) >> 1` |
| `5` | Bateria **Esquerdo (L)** | `charging = (b[5] & 0x80) != 0`; `percent = b[5] & 0x7F` (`1..100`, `0` ou `>100` = ausente/no estojo fechado) |
| `6` | Bateria **Direito (R)** | Mesma codificação do byte `5` |
| `7` | Bateria **Estojo** | Mesma codificação do byte `5` (`0x00` quando o estojo está fechado sem tampa aberta) |
| `12, 11, 13, 16, 15, 14` | `classMac` | MAC Bluetooth Clássico (6 bytes nessa ordem permutada) |
| `19, 18, 20, 23, 22, 21` | `otherMac` | Segundo MAC Clássico (mesma permutação; se zerado, equivale a `classMac`) |

Implementação de referência: [`src/QcyN50.Protocol/QcyAdvertisement.cs`](src/QcyN50.Protocol/QcyAdvertisement.cs)

---

## 2. Enquadramento JieLi RCSP (`FE DC BA … EF`)

Não existe checksum/CRC; a delimitação usa cabeçalho `FE DC BA`, tamanho `paramLen` (`uint16` **Big-Endian**) e terminador `EF`:

```text
Comando (Host -> Fone):
  FE DC BA | C0 (flags) | opCode | paramLen (2B BE) | sn | paramData... | EF

Resposta / ACK (Fone -> Host):
  FE DC BA | 00 (flags) | opCode | paramLen (2B BE) | status (00=OK) | sn | paramData... | EF

ACK obrigatório para pushes do fone (quando flags == C0):
  FE DC BA | 00 | opCode | 00 02 | 00 | sn | EF
```

**Atenção à dualidade de formatos e endianness:**
- **Camada RCSP / JieLi (`0x03`, `0xC0`, `0xC1`):** **LTV** `[len = 1 + val.Length] [type] [value]`, inteiros em **Big-Endian**.
- **Itens Custom QCY (`opCode = 0xFF`):** **TLV** `[itemId] [valLen] [value]` (sem prefixo `FF <totalLen>` do protocolo `A001`), inteiros internos (auto-off, EQ paramétrico) em **Little-Endian**.

Implementação de referência: [`src/QcyN50.Protocol/RcspFrame.cs`](src/QcyN50.Protocol/RcspFrame.cs), [`JieLiLtv.cs`](src/QcyN50.Protocol/JieLiLtv.cs) e [`QcyTlv.cs`](src/QcyN50.Protocol/QcyTlv.cs).

---

## 3. Mapa de Funções Verificadas no Hardware (`V_0.5.2.1`)

| Função | Transporte / Camada | Frame TX de leitura (`sn=01`) | Formato / Escrita | Status (`V_0.5.2.1`) |
|---|---|---|---|---|
| **Firmware + MTU** | `GET_TARGET_INFO (0x03)` | `FE DC BA C0 03 00 06 01 00 00 20 20 00 EF` | Attr `0x05`: 2 bytes em nibbles (`05 21` → `V_0.5.2.1`); Attr `0x0D`: `sendMtu (u16 BE)`, `recvMtu (u16 BE)` | Confirmado |
| **Bateria + WorkMode + Sono** | `ADV_GET (0xC1)` mask `0x00000D21` | `FE DC BA C0 C1 00 05 01 00 00 0D 21 EF` | LTV `0x00`: `[L, R, Case]` (bit 7 = carga); `0x05`: `01`=Normal / `02`=Jogo; `0x0A`: `01`=Sono ON / `02`=OFF | Confirmado |
| **Nome do fone** *(leitura)* | `ADV_GET (0xC1)` attr `0x01` | `FE DC BA C0 C1 00 05 01 00 00 00 02 EF` | LTV `0x01`: UTF-8 (`"QCY MeloBuds N50"`) | Confirmado |
| **ANC / Transparência / Normal** | `Custom (0xFF)` item `0x17` | `FE DC BA C0 FF 00 04 01 FE 01 17 EF` | `17 03 <mode> <scene> <level>` — Normal `02 00 00`; ANC Adaptativo `01 05 00`; Cenas `01`/`02`/`03` com força `00..02`; Anti-vento `01 04 00`; Transparência `03 01 00..06` *(nível `0` remapeado para `03 02 00`)* | Confirmado *(cooldown mínimo ~1,5 s entre trocas!)* |
| **Modo Jogo (68 ms)** | `ADV_GET/SET (0xC1/0xC0)` attr `0x05` | `FE DC BA C0 C1 00 05 01 00 00 00 20 EF` | Escrita: `FE DC BA C0 C0 00 04 01 02 05 02 EF` (ON) / `... 01 EF` (OFF) | Confirmado |
| **Modo Sono** | `ADV_GET/SET (0xC1/0xC0)` attr `0x0A` | `FE DC BA C0 C1 00 05 01 00 00 04 00 EF` | Escrita: `... 02 0A 01 EF` (ON) / `... 02 0A 02 EF` (OFF) | Confirmado |
| **Multiponto (2 dispositivos)** | `Custom (0xFF)` item `0x24` | `FE DC BA C0 FF 00 04 01 FE 01 24 EF` | Escrita: `... 24 01 01 EF` (ON) / `... 24 01 02 EF` (OFF) | Confirmado |
| **Balanço L / R** | `ADV_GET/SET (0xC1/0xC0)` attr `0x0C` | `FE DC BA C0 C1 00 05 01 00 00 10 00 EF` | `0..100` (`50 = 0x32` centro) | Confirmado |
| **Desligamento automático** | `Custom (0xFF)` item `0x14` | `FE DC BA C0 FF 00 04 01 FE 01 14 EF` | `14 04 <min_u16_LE> 00 00` (`FF FF` = desativado; **nunca enviar `0`**!) | Confirmado |
| **Volume dos avisos** | `Custom (0xFF)` item `0x1D` | `FE DC BA C0 FF 00 04 01 FE 01 1D EF` | Resposta `1D 02 <vol> <maxVol>` (`08 08`); escrita `1D 02 <vol> 00` | Confirmado |
| **Mapeamento de toques** | `ADV_GET/SET (0xC1/0xC0)` attr `0x02` | `FE DC BA C0 C1 00 05 01 00 00 00 04 EF` | 20 triplas `[side, gesture, action]` (10 binaural + 10 modo 1 fone `0x81..0x85`; `0x0B` = Alternar ANC, `0x7F` = Nenhuma) | Confirmado |
| **Equalizador Paramétrico v2** | `Custom (0xFF)` item `0x22` | `FE DC BA C0 FF 00 04 01 FE 01 22 EF` | `22 <len> <eqIdx> <master_s16_LE> [freq_u16_LE, gain_s16_LE, Q_u16_LE, filterType_1B] × N` (ganho e Q em centésimos `×100`). *Nota: `GET_SYS_INFO 0x07` (EQ gráfico) retorna vazio no `V_0.5.2.1`.* | Confirmado |
| **Encontrar fones** | **Áudio A2DP local (sem BLE)** | *(nenhum comando RCSP)* | Tocar bipe na saída A2DP dos fones com pan L/R. | Confirmado via APK |

> **Descoberta crítica de firmware (`V_0.5.2.1`):** trocas de ANC (`Custom 0x17`) enviadas com intervalo inferior a **~1,5 s** são silenciosamente descartadas pelo firmware. Qualquer cliente deve aplicar **espaçamento mínimo de 1,5 s + fila *latest-selection-wins* + confirmação por read-back dos 3 bytes**.

Consulte a especificação completa (incluindo divergências entre APK, PairFree, Quicky e OpenQCY, payloads reais e lista negra de segurança) em **[`docs/PROTOCOLO_N50.md`](docs/PROTOCOLO_N50.md)**.

---

## 4. Segurança (`ProtocolGuard`)

Toda saída gerada por `N50Commands` passa por [`src/QcyN50.Protocol/ProtocolGuard.cs`](src/QcyN50.Protocol/ProtocolGuard.cs), que opera em modo ***default-deny*** e bloqueia explicitamente comandos destrutivos ou não mapeados:
- Opcodes proibidos: `0x0E`, `0xC5` (factory reset / clear pairing), `0xE7` (reboot), `0xD4` / `0xE1..0xE8` (OTA/DFU).
- `ADV_SET (0xC0)`: proibido gravar `DEVICE_NAME (1)`, `LED (3)`, `MIC (4)`, `PRODUCT (6)`, `LANGUAGE (9)`.
- `Custom (0xFF)`: proibidos `0x01`, `0x02`, `0x03`, `0x0D`, `0x18`, `0x1E`, `0x1F`, `0x25`, `0x26`, `0x34`, `0x3E`, `0x3F` e timer `0x14` com `minutes == 0`.

---

## 5. Estrutura do Repositório e Como Usar

```text
docs/
  PROTOCOLO_N50.md              # Especificação completa byte-a-byte (verificada em hardware)
  REFERENCIAS.md                # Fontes consultadas e política clean-room
src/
  QcyN50.Protocol/              # Biblioteca .NET 8 pura (zero dependências de UI/Windows)
    RcspFrame.cs                # Framer + reassemblagem de pacotes FE DC BA ... EF
    JieLiLtv.cs / QcyTlv.cs     # Codecs LTV (JieLi) e TLV (QCY Custom 0xFF)
    QcyAdvertisement.cs         # Parser do anúncio passivo BLE 0x521C
    QcyUuids.cs                 # UUIDs RFCOMM SPP, GATT A002 e Company ID 0x521C
    N50Models.cs                # Modelos imutáveis de estado
    N50Commands.cs              # Construtores de todos os frames de leitura/escrita
    N50Parsers.cs               # Parsers de respostas TargetInfo, ADV, EQ, ANC, etc.
    N50EqPresets.cs             # Curvas de presets de 10 bandas e ganho mestre seguro
    ProtocolGuard.cs            # Allowlist estrita contra comandos destrutivos
tests/
  QcyN50.Protocol.Tests/        # 48 testes unitários com capturas reais do N50 V_0.5.2.1
tools/
  N50.Probe/                    # CLI Windows (WinRT) para escanear BLE 0x521C e sondar via RFCOMM SPP
```

### Rodar os 48 testes unitários (Windows, Linux ou macOS)

```bash
dotnet test tests/QcyN50.Protocol.Tests/QcyN50.Protocol.Tests.csproj -c Release
```

### Usar o CLI `N50.Probe` no Windows (somente leitura e testes reversíveis)

```powershell
# 1. Ler bateria passivamente via BLE Advertising 0x521C (sem conectar) e listar pareados
dotnet run --project tools/N50.Probe -- scan 10

# 2. Sondagem 100% SOMENTE LEITURA via RFCOMM SPP (descobre "QCY MeloBuds N50" automaticamente ou aceita <mac>)
dotnet run --project tools/N50.Probe -- probe-spp
dotnet run --project tools/N50.Probe -- probe-spp AA:BB:CC:DD:EE:FF

# 3. Escutar notificações espontâneas (ex.: pushes 0xC2 / 0x32 ao tocar na haste)
dotnet run --project tools/N50.Probe -- spp-watch 20

# 4. Alternar ANC reversivelmente (ex.: Adaptativo 01 05 00 | Transparência 03 01 04 | Normal 02 00 00)
dotnet run --project tools/N50.Probe -- spp-set-anc 01 05 00
```

---

## Licença e Créditos

Distribuído sob licença **MIT** ([LICENSE](LICENSE)). Veja [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) e [docs/REFERENCIAS.md](docs/REFERENCIAS.md) para detalhes de conformidade *clean-room* e atribuições.
