# Third-party notices

## Bundled in the Windows executable

`Atlasmith.exe` is a self-contained .NET application. It bundles the .NET runtime
and the Windows Desktop runtime (WPF), © .NET Foundation and Contributors, under
the MIT License. Their own third-party notices:

- https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- https://github.com/dotnet/wpf/blob/main/THIRD-PARTY-NOTICES.TXT

No other third-party code is included in the executable or in the Unity package.

## Algorithms and formats

These were implemented from their published descriptions; no code was copied.

- **MaxRects** rectangle packing: Jukka Jylänki, *A Thousand Ways to Pack the Bin —
  A Practical Approach to Two-Dimensional Rectangle Bin Packing* (2010).
- **LZF** decompression, as used for Krita's layer tiles: the format of Marc
  Lehmann's liblzf.
- **Krita** `.kra`/`.krz` documents: read from the file format as Krita saves it.
- **`.tpsheet`**: Atlasmith can write sprite sheets in the layout read by
  CodeAndWeb's TexturePackerImporter, so existing Unity projects keep working. It
  does not include, modify or redistribute TexturePacker or TexturePackerImporter.

## Trademarks

TexturePacker is a trademark of CodeAndWeb GmbH. Krita is a trademark of the Krita
Foundation. Unity is a trademark of Unity Technologies. Windows is a trademark of
Microsoft Corporation. Atlasmith is not affiliated with or endorsed by any of them.
