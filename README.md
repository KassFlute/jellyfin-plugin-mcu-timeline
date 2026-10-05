# MCU Timeline

[![Build](https://github.com/KassFlute/jellyfin-plugin-mcu-timeline/actions/workflows/build.yaml/badge.svg)](https://github.com/KassFlute/jellyfin-plugin-mcu-timeline/actions/workflows/build.yaml)
[![Jellyfin](https://img.shields.io/badge/jellyfin-12.1-00a4dc)](https://jellyfin.org)
[![License](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

Jellyfin plugin that shows the whole Marvel Cinematic Universe on one timeline, in release
order or in story order. Titles you own show their poster, play state and a play button.
Titles you do not own, or that are not out yet, stay on the timeline, greyed out.

It also keeps two playlists, "MCU : ordre de sortie" and "MCU : ordre chronologique", so TV
and mobile clients that cannot show the timeline get the same viewing orders.

The interface is in French.

## Install

Requires Jellyfin 12.1.

In Dashboard, Plugins, Repositories, add:

```
https://raw.githubusercontent.com/KassFlute/jellyfin-plugin-mcu-timeline/main/manifest.json
```

Install MCU Timeline from the catalogue and restart Jellyfin.

## The MCU tab

The timeline is a page of its own, at `<server>/McuTimeline/page`. To show it as a tab in
the web client, install [Custom Tabs](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs)
(0.3.1.0 or later for Jellyfin 12.1) and add a tab with this HTML content:

```html
<iframe src="../McuTimeline/page" title="MCU" style="width:100%;height:calc(100vh - 5rem);border:0;display:block"></iframe>
```

The plugin settings page shows the same snippet.

The page itself holds no data. It uses the session of the web client it runs in, and every
call it makes requires a signed in user, like the rest of Jellyfin. Opened without a
session, it asks you to sign in. The play button starts playback in that same web client.
Opened outside the web client, it opens the item page instead.

## Playlists

Both playlists are owned by the user picked in the settings (the first administrator by
default) and open to every user. Play state stays per user. They hold owned titles only. A
series entry adds its episodes, limited to the seasons the entry lists.

They are recomputed after each library scan, after titles are added or removed, once a day
through the "Synchronise MCU playlists" scheduled task, and from the settings page. A
playlist deleted by hand comes back on the next sync. The plugin only ever touches the two
playlists whose ids it recorded.

## Data

The timeline comes from `src/Jellyfin.Plugin.McuTimeline/Data/mcu-timeline.json`, shipped in
the plugin. Titles are matched with the library by TMDB id, then IMDb id, never by title. The
settings page lists the titles it could not find.

To fix an order without a new release, drop a file with the same format at
`<config>/plugins/configurations/mcu-timeline.json`. It replaces the shipped file as long as
it is valid, and is picked up without a restart. An invalid file is ignored, with the reason
on the settings page and in the log.

```json
{
  "version": "2026.10.1",
  "items": [
    {
      "id": "captain-america-first-avenger",
      "title": "Captain America : First Avenger",
      "type": "movie",
      "tmdbId": 1771,
      "releaseDate": "2011-07-22",
      "chronoOrder": 10,
      "storyYear": "1943-1945",
      "phase": 1,
      "saga": "infinity",
      "era": "origins"
    }
  ]
}
```

| Field | Required | |
|---|---|---|
| `id` | yes | Stable identifier, unique in the file. |
| `title` | yes | Displayed title. |
| `type` | yes | `movie`, `series` or `short`. A short is matched against movies. |
| `tmdbId` | yes | TMDB id of the movie or series. |
| `imdbId` | no | Tried when the TMDB id finds nothing. |
| `seasons` | no | Series only. Seasons this entry covers. Without it, every season but specials. |
| `releaseDate` | yes | `yyyy-MM-dd`. Release order, and "upcoming" until that day. |
| `chronoOrder` | yes | Story order. Steps of 10 leave room to insert. |
| `storyYear` | no | In-universe year shown on the card, never used for grouping. |
| `phase` | yes | 1 to 6. |
| `saga` | yes | `infinity` or `multiverse`. Separators in release order. |
| `era` | yes | `origins`, `avengers`, `fracture`, `blip` or `post-blip`. Separators in story order. |
| `note` | no | One line explaining a debatable placement. |

Release order groups by saga then phase. Story order groups by era only, since a title can
belong to one saga and take place in another era.

## Build

```
dotnet test tests/Jellyfin.Plugin.McuTimeline.Tests
./scripts/package.sh
```

The package lands in `artifacts/`. Unzip it into `<config>/plugins/` and restart Jellyfin.
