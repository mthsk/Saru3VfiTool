 # Saru3VfiTool

**The Swiss Army Knife of Ape Escape 3 modding.**

A cross-platform command-line toolkit for extracting and rebuilding the `DATA.BIN` container used by *Ape Escape 3* (PlayStation 2). Handles the full pipeline: VFI archive structure → `.sz` decompression → `.pck` expansion.

> **Legal notice:** This tool is for personal study and clean-room analysis only. All extracted data is the property of Sony Computer Entertainment. Do not redistribute extracted game assets.

---

## Downloads

Pre-built binaries are available from the [Releases](../../releases) page for:

| Platform | Architecture |
|----------|-------------|
| Windows | x64 |
| Linux | x64 |
| macOS | Intel (x64) & Apple Silicon (arm64) |

Requires the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or use the self-contained builds in Releases).

---

## Supported formats

| Stage | Format | Status |
|-------|--------|--------|
| VFI container | `DATA.BIN` | ✅ Full extract & rebuild |
| Compression | `.sz` (raw DEFLATE) | ✅ Auto decompress / recompress |
| Sub-container | `.pck` | ✅ Expand to loose files + `.pck.json` metadata |
| Textures | `.tm2` → PNG | 🧪 Experimental round-trip |
| Models | `.i3d` (I3D_BIN) | ✅ Extracted raw (no conversion) |
| Audio / FMV | `.str`, `.x`, `.hd`/`.bd`/`.mid` | ✅ Extracted raw |

---

## Usage

### Extract

```bash
Saru3VfiTool extract [--tim2] [--keep-pck] [--keep-sz] <DATA.BIN> <output_dir>
```

| Flag | Effect |
|------|--------|
| `--tim2` | Convert TIM2 textures to PNG with `.tm2.json` sidecars (experimental) |
| `--keep-pck` | Leave `.pck` files as opaque blobs (do not expand members) |
| `--keep-sz` | Leave `.sz` files compressed (skip DEFLATE decompression) |

**Example:**

```bash
Saru3VfiTool extract --tim2 DATA.BIN ./extracted
```

Produces a full directory tree plus a `databin.manifest.json` required for rebuilding.

### Rebuild

```bash
Saru3VfiTool rebuild <databin.manifest.json> <output.bin>
```

Reads the manifest and all sidecar metadata (`.pck.json`, `.tm2.json`) to reconstruct `DATA.BIN`.

**Example:**

```bash
Saru3VfiTool rebuild ./extracted/databin.manifest.json ./DATA_REBUILT.BIN
```

---

## Important limitations

1. **Directory structure must remain unchanged.**  
   Adding, removing, or renaming folders will break the rebuilt archive. Only modify the *contents* of existing folder.

2. **TIM2 ↔ PNG conversion is experimental.**  
   Round-tripping textures through PNG may not perfectly preserve all TIM2 features (CLUT modes, mipmaps, special flags). Always test in-game after editing textures.

---

## Project structure after extraction

```
extracted/
├── databin.manifest.json      # VFI archive manifest (required for rebuild)
├── debug/
│   └── us/
│       ├── stage/
│       │   └── seaside_a/
│       │       ├── bg.pck.json
│       │       ├── bg/
│       │       │   ├── a_tvcar.i3d
│       │       │   ├── f_tvcar01.tm2
│       │       │   └── f_tvcar01.tm2.json   # if --tim2
│       │       └── ...
│       └── sound/
│           └── bgm/
│               └── ...
└── irx/
    └── 3.0/
        └── ...
```
---

## Building from source

```bash
dotnet build -c Release
```

Dependencies (restored automatically via NuGet):

- `SkiaSharp` — PNG encoding/decoding for TIM2 conversion
- `Newtonsoft.Json` — Manifest and metadata serialization

---

## License

GNU GPL v3. See [LICENSE](LICENSE) or <https://www.gnu.org/licenses/gpl-3.0.html>.

---

## Acknowledgements

- **aluigi** - Independent prior art (`ape_escape_vfi.bms`) that cross-validated the VFI structure.
- **Durik256 / ZenHAX / ResHax** - Noesis plugins and research on the `I3D_BIN` model format.
- **[@pxdl](https://github.com/pxdl)** - This tool is mostly based on his de-compilation research. Couldn't have done it without him.
