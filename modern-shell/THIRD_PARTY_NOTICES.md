# Third-Party Notices

## AutoHotkey v2.0.29

This project bundles the unmodified AutoHotkey v2.0.29 portable runtime as a separate executable payload inside the modern shell.

- Upstream project: https://github.com/AutoHotkey/AutoHotkey
- Source tag: https://github.com/AutoHotkey/AutoHotkey/tree/v2.0.29
- License: GNU General Public License v2.0
- Bundled archive: `AutoHotkey_2.0.29.zip`
- SHA-256: `B2D0200724A6B6AD22C965C939C5E5A2C64A35D1CCB455A3CA3F8CE415C5A296`
- Official download directory: https://www.autohotkey.com/download/2.0/

The runtime remains a separate native executable and is not linked into the C# application.

Before distributing production binaries, verify that the release package and project distribution process continue to satisfy the upstream GPL-2.0 source-availability requirements.

## SharpCompress 0.50.3

The modern shell bundles SharpCompress for ZIP, 7z, RAR, TAR and compressed TAR extraction. No separate archive application is required.

- Upstream project: https://github.com/adamhathcock/sharpcompress
- Source tag: https://github.com/adamhathcock/sharpcompress/tree/0.50.3
- License: MIT; upstream notice: https://github.com/adamhathcock/sharpcompress/blob/0.50.3/LICENSE.txt
- Dependencies retain their own upstream licenses; the NuGet package metadata identifies them.
