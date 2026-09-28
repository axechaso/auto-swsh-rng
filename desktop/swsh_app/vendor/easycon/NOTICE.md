# Native EasyCon source attribution

These Python modules originate from
[XiaoyuBook/auto-bdsp-rng](https://github.com/XiaoyuBook/auto-bdsp-rng/tree/eb1e65226fe678dd6e30f48e5d5fbbdeec566ba8/src/auto_bdsp_rng/automation/easycon/native),
commit `eb1e65226fe678dd6e30f48e5d5fbbdeec566ba8`, under GPL-3.0-or-later.
See `LICENSE.txt` and the application's GPL license.

Local adaptation: import namespace changed to `swsh_app.vendor.easycon`;
Tesseract resource lookup is routed through `swsh_app.resources`; the Windows
Tesseract data path uses the system code page (or a short path) to support
Chinese installation directories with the bundled narrow-path DLL.
The device, protocol, ECS parser/evaluator and image-label implementations are otherwise preserved.

`swsh_app.controller` provides the Sword/Shield session owner. Qt capture and OCR
use a shared raw-frame store; no BDSP game-specific scripts or ROI coordinates
are used as Sword/Shield defaults.
