# Third-Party Notices & Attribution

This repository is an independent clean-room protocol specification and reference implementation for communicating with **QCY MeloBuds N50 (HT12)** earbuds.

## Clean-Room & Copyleft Compliance Statement

- **Zero source code** from GPL-3.0, GPLv3, AGPL-3.0, or unlicensed projects (`PairFree`, `Quicky`) is copied, ported, or linked into this repository. Only non-copyrightable interoperability facts (wire formats, model tables, test vectors) were consulted and cross-checked against static analysis and real hardware captures.
- No proprietary binaries, firmware images, decompiled code, or assets from the official QCY Android application are included in this repository.
- Whenever patterns from permissively licensed projects (MIT) were adapted in `tools/N50.Probe`, top-of-file attribution comments identify the upstream repository and license.

---

## Permissively Licensed Projects Referenced (MIT License)

### OpenQCY-Desktop (MIT License)
- Repository: https://github.com/GiorgioRafael/OpenQCY-Desktop
- Copyright (c) 2026 GiorgioRafael
- Usage: Interoperability reference (QCY custom TLV comparisons, battery fallback hierarchy, ANC latest-selection-wins queue semantics).

### JblTray (MIT License)
- Repository: https://github.com/emperorbug46/JblTray
- Copyright (c) 2026 emperorbug46
- Usage: Windows WinRT Bluetooth timeout wrapper (`TryOp`) and LE advertisement cache patterns in `tools/N50.Probe`.

---

## MIT License Text (applies to MIT references above)

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
