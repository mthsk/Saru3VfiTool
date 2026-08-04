# Ape Escape 3 / Saru! Get You! 3 VFI Tool

**The Swiss Army Knife of _Ape Escape 3_ / _Saru! Get You! 3 (サルゲッチュ3)_ modding.**

A cross-platform command-line toolkit for extracting, converting, editing, and rebuilding assets from the PlayStation 2 game. It supports both complete `DATA.BIN` workflows and round-trip processing of individual files.

> **Legal notice:** This tool is for personal study and clean-room analysis only. All extracted data is the property of Sony Computer Entertainment. Do not redistribute extracted game assets.

---

## Downloads

Pre-built binaries are available from the [Releases](../../releases) page for Windows, Linux, and macOS.

Requires the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0), unless using a self-contained release.

---

## Supported formats

| Format | Drop/process result | Reverse operation |
|---|---|---|
| `DATA.BIN` / VFI | Full extraction, including SZ/PCK/TIM2/EXDB processing | Drop `databin.manifest.json` or the extraction directory |
| `.sz` | Decompressed payload, automatic inner conversion when supported, plus `.sz.json` | Drop the `.sz.json` sidecar |
| `.pck` | Loose members in `.pck.d` plus `.pck.json` | Drop the `.pck.json` sidecar or `.pck.d` directory |
| `.tm2` | PNG, mipmap PNGs, and `.tm2.json` | Drop the `.tm2.json` sidecar or main PNG |
| EXDB / EDB | Editable `.exdb.json` document | Drop the JSON document |
| Other assets | Preserved as raw files inside extracted containers | Reinserted during rebuild |

EXDB support is based on the self-describing EDB layout documented by `ae3-sdk/tools/ae3tools/exdb.py`: `s`, `f`, and `i` fields map to strings, little-endian `float32`, and little-endian `int32` values. The JSON stores the original header, raw record bytes, duplicate-field aliases, and trailing data to make round trips as conservative as possible.

---

## Drag and drop

Drag one or more supported files onto `Saru3VfiTool.exe`. Each path is detected independently by extension, magic, or JSON `type`.

Typical results:

```text
texture.tm2       -> texture.png + texture.tm2.json
texture.tm2.json  -> texture.tm2
archive.pck       -> archive.pck.d/ + archive.pck.json
archive.pck.json  -> archive.pck
file.sz           -> file + file.sz.json (+ inner sidecars when supported)
file.sz.json      -> rebuilt inner format -> file.sz
params.exdb       -> params.exdb.json
params.exdb.json  -> params.exdb
DATA.BIN          -> DATA.extracted/
```

Dropping a PNG rebuilds TIM2 only when a matching `<name>.tm2.json` sidecar is present. Dropped `.sz` files continue into TIM2, PCK, EXDB, or VFI conversion when their decompressed payload is recognized; the SZ sidecar points at that editable nested sidecar so repacking includes the edits.

---

## Command-line usage

### Auto-detect an individual file

```bash
Saru3VfiTool process <input> [output]
```

`auto`, `convert`, `unpack`, and `repack` are aliases of `process`; the actual action is determined from the input.

Examples:

```bash
Saru3VfiTool process texture.tm2
Saru3VfiTool process archive.pck
Saru3VfiTool process params.exdb
Saru3VfiTool process params.exdb.json rebuilt.exdb
```

### Extract complete DATA.BIN

```bash
Saru3VfiTool extract [--tim2] [--keep-pck] [--keep-sz] [--keep-exdb] <DATA.BIN> <output_dir>
```

| Flag | Effect |
|---|---|
| `--tim2` | Convert TIM2 textures to PNG with `.tm2.json` sidecars |
| `--keep-pck` | Leave `.pck` files as opaque blobs |
| `--keep-sz` | Leave `.sz` files compressed |
| `--keep-exdb` | Keep EXDB files binary instead of converting them to editable JSON |

```bash
Saru3VfiTool extract --tim2 DATA.BIN ./extracted
```

EXDB files are converted to `.exdb.json` by default during VFI extraction, including EXDB payloads revealed by SZ decompression and EXDB members found inside expanded PCK files. Use `--keep-exdb` to preserve those files as binary.

### Rebuild complete DATA.BIN

```bash
Saru3VfiTool rebuild <databin.manifest.json> <output.bin>
```

```bash
Saru3VfiTool rebuild ./extracted/databin.manifest.json ./DATA_REBUILT.BIN
```

---

## Sidecar rules

Sidecars are part of the round-trip format and should remain beside their generated files.

- TIM2 JSON preserves texture format, CLUT settings, GS registers, mipmap metadata, and user data.
- PCK JSON preserves member names, attributes, order, and source paths. TIM2 and EXDB members are converted recursively when possible.
- SZ JSON records the active decompressed source and original sizes. When the payload has a supported format, that source is its editable nested sidecar so SZ repacking rebuilds the inner file first.
- EXDB JSON puts editable records first, with each record's named fields before `_raw`. It preserves the original header block and raw record bytes so edits patch known values while retaining unknown gaps.

Files can be moved together as a group because sidecar source paths are relative.

---

## Important limitations

1. **TIM2 conversion remains experimental.** Indexed textures may require quantization, and unusual CLUT modes, mipmaps, or flags should be tested in-game.
2. **EXDB schemas are self-describing but not fully validated against game code.** The converter supports the observed `s`, `f`, and `i` field types. Keep backups before changing record counts or schema metadata.
3. **Container rebuilds are structural, not guaranteed byte-identical.** PCK alignment and SZ DEFLATE output may differ while decoding to equivalent data.
4. **Complete VFI rebuilds still depend on the generated manifest and directory structure.** Avoid renaming or deleting extracted paths unless you understand the archive references.

---

## Building from source

```bash
dotnet build -c Release
```

Dependencies are restored through NuGet:

- `SkiaSharp` for PNG/TIM2 conversion
- `Newtonsoft.Json` for manifests and editable JSON

---

## License

GNU GPL v3. See [LICENSE](LICENSE).

---

## Acknowledgements

- **aluigi** — independent VFI research (`ape_escape_vfi.bms`)
- **Durik256 / ZenHAX / ResHax** — I3D format research and Noesis tooling
- **[@pxdl](https://github.com/pxdl)** — decompilation research and the EXDB parser used as the format reference
