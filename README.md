# gmsb-codec-mp3

MP3 playback codec plugin for [Game Master Sound Board](https://github.com/DevinSanders/game-master-soundboard).

Adds `.mp3` support via the pure-managed
[NLayer](https://github.com/naudio/NLayer) MPEG decoder. No native binaries
to ship — the plugin folder is just two managed DLLs.

## Install

Drop the released `.zip` onto Settings → Plugin Manager in Game Master
Sound Board. The host installs it and prompts you to restart. After
the restart, enable **MP3 Codec** under Settings → Plugins.

Pre-built zips are attached to each [GitHub Release](../../releases).

## Build

Requires .NET 10 SDK. `SoundBoard.PluginApi` is restored from NuGet, so no sibling checkout is required. To build against a local, unreleased SDK you can optionally check out the main `Game Master Sound Board` repo beside this one — optional layout:

```
D:\My Projects\
├── Game Master Sound Board\
└── gmsb-codec-mp3\          ← this repo
```

Then:

```powershell
dotnet build src/Mp3CodecPlugin.csproj
pwsh scripts/package.ps1
# → dist/github.DevinSanders-codec.mp3-1.0.0.zip
```

Releases are cut by pushing a `v<semver>` tag (e.g. `git tag v1.0.0 && git push origin v1.0.0`). The release workflow derives the version from the tag, stamps it into both `plugin.json` and the plugin assembly, and attaches the resulting zip to the GitHub Release.

## Plugin manifest

| Field     | Value                       |
|-----------|-----------------------------|
| publisher | `github.DevinSanders`       |
| id        | `codec.mp3`                 |
| entryDll  | `Mp3CodecPlugin.dll`        |
| isTheme   | `false`                     |

## License

Released under the [MIT License](LICENSE).

Third-party components used by this plugin:

- NLayer (MIT) for pure-managed MP3 decoding.