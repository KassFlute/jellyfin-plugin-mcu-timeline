# MCU Timeline

![MCU Timeline](screenshot.png)

A Jellyfin plugin that shows the whole Marvel Cinematic Universe on one timeline, in release or
story order, and keeps two matching playlists for the apps that cannot show the timeline.

Requires Jellyfin 10.11.

## Install

The timeline page needs [Plugin Pages](https://github.com/IAmParadox27/jellyfin-plugin-pages)
and [File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation).

1. In Dashboard, Plugins, Repositories, add
   `https://raw.githubusercontent.com/KassFlute/jellyfin-plugin-mcu-timeline/main/manifest.json`
2. Install **MCU Timeline** from the catalogue and restart Jellyfin.
3. In the plugin settings, tick "Show the timeline under Media in the side menu".

## Settings

- **Libraries**: where to look for the titles.
- **Jellyseerr**: address and API key, to request missing titles.
- **Playlists**: owner and names.

## Data

To change the timeline, copy
[`mcu-timeline.json`](src/Jellyfin.Plugin.McuTimeline/Data/mcu-timeline.json) to
`<config>/plugins/configurations/mcu-timeline.json` and edit it. It replaces the shipped data,
no restart needed.

Disagree with an order or a placement? Open an issue.

## Build

```
dotnet test tests/Jellyfin.Plugin.McuTimeline.Tests
./scripts/package.sh
```

The zip lands in `artifacts/`. Unzip it into `<config>/plugins/` and restart Jellyfin.

## License

GPL-3.0
