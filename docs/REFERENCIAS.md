# Referências Consultadas e Política de Uso

Este arquivo cataloga as fontes consultadas na pesquisa e validação do protocolo do **QCY MeloBuds N50 (HT12)**, deixando explícitos a licença e o tipo de uso autorizado de cada referência.

---

## 1. Regras de Conformidade Adotadas

| Categoria de licença | Regra aplicada neste repositório |
|---|---|
| **MIT / Apache-2.0** | Permite adaptar padrões ou trechos de código, mantendo aviso de copyright no cabeçalho do arquivo adaptado e em [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md). |
| **GPL-3.0 / GPLv3 / AGPL-3.0** | **Somente fatos de protocolo, tabelas de modelos e vetores de teste.** Nenhum código-fonte é copiado ou portado. |
| **Sem licença** | **Somente fatos.** Nenhum código-fonte ou trecho é copiado. |

---

## 2. Fontes do Protocolo QCY / MeloBuds N50

| Projeto / Fonte | URL / Origem | Licença | Tipo de uso | Observações |
|---|---|---|---|---|
| **APK Oficial QCY v4.0.7** | `com.qcymall.googleearphonesetup` (`qcy-4-0-7.apk`, SHA-256 `fe671386784a064601c24dca25d6a55c8782696c3899afed0dfc35c89998648d`) | Proprietário (análise para interoperabilidade) | **Fatos** (análise estática via REA + JADX) | Fonte primária de `Devicebind`, `JLDeviceImpl`, `ParseHelper`, `QCYConnectManager` e `CommandBuilder`. Nenhum binário ou código do APK é redistribuído. |
| **PairFree** | https://github.com/Omega-Destroyer/PairFree | GPL-3.0-or-later | **Fatos + Vetores de teste** (zero código copiado) | Cruzamento de `docs/qcy/PROTOCOL.md` e `data/qcy/*.json`. |
| **OpenQCY-Desktop** | https://github.com/GiorgioRafael/OpenQCY-Desktop | **MIT** | **Fatos + Padrões adaptáveis** | Comparação de itens custom QCY (`0x17`, `0x22`, `0x24`, `0x14`, `0x1D`) e fila *latest-selection-wins* de ANC. |
| **Quicky** | https://github.com/hui1601/Quicky | **Sem licença** | **Somente fatos** (zero código) | Catálogo de recursos das entradas `18298` (`0x477A`) e `18300` (`0x477C`); divergência de máscara `colorIndex` corrigida via APK. |
| **JblTray** | https://github.com/emperorbug46/JblTray | **MIT** | **Padrões WinRT Bluetooth** | Timeout seguro (`TryOp`) e leitura de anúncios BLE em `tools/N50.Probe`. |
| **Hardware Real (N50 `0x477A`, `V_0.5.2.1`)** | Capturas reais via RFCOMM SPP + BLE `0x521C` | — | **Validação empírica** | Confirmou RFCOMM SPP `00001101`, EQ Paramétrico v2 (`0x22`), cooldown de ~1,5 s no ANC, push `0x32` e tabela completa de 20 gestos de toque. |
