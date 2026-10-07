# MCU Timeline

![MCU Timeline](logo.png)

Brings the Marvel Cinematic Universe into Jellyfin. The plugin knows every MCU film, series and
short, in release order and in story order, and matches them with your library. You pick how it
shows up:

- **Timeline page**: a page of its own, with Marvel filters, watch tracking and requests.
- **Playlists**: one per order, to watch the MCU straight through on any Jellyfin app.
- **Collection**: every MCU title of your library in one place.

Use one, two or all three. Works on Jellyfin 10.9, 10.10, 10.11 and 12.

## Timeline page

![Timeline page](screenshot.png)

The whole MCU on one timeline, under Media in the side menu.

- Release order or story order, grouped by phase or by era.
- Filters by phase, by type (movies, series, shorts) and to what you own.
- Watch tracking across the MCU: what to watch next, a progress line, titles marked as seen
  elsewhere when the library lacks them, titles skipped from the run.
- Request a missing title through Jellyseerr, in one click.

Needs [Plugin Pages](https://github.com/IAmParadox27/jellyfin-plugin-pages) and
[File Transformation](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation).
They have no build for Jellyfin 10.9, so the page is not available there.

## Playlists

**MCU: release order** and **MCU: story order**, shared with every user. Series come in as
their episodes, each season at its place in the order. Any Jellyfin app can play them.

## Collection

**Marvel Cinematic Universe**, with every MCU film and series of the library, in no particular
order. It gets an MCU poster and backdrop, unless you pick other images in Jellyfin.

## Install

1. In Dashboard, Plugins, Repositories, add
   ```
   https://raw.githubusercontent.com/KassFlute/jellyfin-plugin-mcu-timeline/main/manifest.json
   ```
2. Install **MCU Timeline** from the catalogue and restart Jellyfin.
3. In the plugin settings, under **Show in Jellyfin**, tick at least one of the timeline page,
   the playlists or the collection, and save. Everything is off after install, so until then
   the MCU shows nowhere in Jellyfin.

## Settings

- **Show in Jellyfin**: the timeline page, each playlist and the collection, each on its own.
  Saving creates what is ticked and deletes the playlists and the collection that are not.
  They are kept up to date when the library changes, and once a day.
- **Libraries**: where to look for the titles.
- **Shorts** and **upcoming titles**: whether to include them.
- **Jellyseerr**: address and API key, for the request button of the timeline page.
- **Names**: playlist owner, playlist and collection names.

## Data

To change the order or the titles, copy
[`mcu-timeline.json`](src/Jellyfin.Plugin.McuTimeline/Data/mcu-timeline.json) to
`<config>/plugins/configurations/mcu-timeline.json` and edit it. It replaces the shipped data,
no restart needed.

Disagree with an order or a placement? Open an issue.

## Build

```
dotnet test tests/Jellyfin.Plugin.McuTimeline.Tests
./scripts/package.sh          # Jellyfin 10.11
./scripts/package.sh 12       # or 10.10, 10.9
```

The zip lands in `artifacts/`. Unzip it into `<config>/plugins/` and restart Jellyfin.

Each release ships one build per Jellyfin version, numbered with the Jellyfin minor as last
part: 1.1.0.9, 1.1.0.10, 1.1.0.11, and 1.1.0.12 for Jellyfin 12. Tag the GitHub release with
the first three parts.

## License

GPL-3.0
