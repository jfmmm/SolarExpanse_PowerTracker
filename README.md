# PowerTracker

A BepInEx mod for Solar Expanse that adds a real-time power overview panel.

## Features

- **Power overview panel** — per-body GEN/DAY, CONS/DAY, BALANCE, and BATTERY at a glance
- **Expandable facility rows** — drill into each body to see per-facility generation, fuel consumption, workers, supply days, and stock
- **Transmitter / receiver display** — expand energy transfer and receiver facilities to see linked bodies, transfer ratios, and power values
- **Alert thresholds** — configurable warn/crit thresholds for energy balance deficit and fuel supply days, with per-body and per-facility overrides
- **Severity colouring** — balance and supply columns colour green/orange/red based on your thresholds

## Installation

1. Install [BepInEx](https://github.com/BepInEx/BepInEx) for Solar Expanse.
2. Drop `PowerTracker.dll` into `BepInEx/plugins/`.
3. Launch the game. A **POWER** button appears next to the notification button.

## Usage

Click **POWER** to open/close the panel. Click a body row to expand its facilities. Click a transmitter or receiver facility row to expand its power links.

Threshold settings are accessible via the **ALERT THRESHOLDS** tab inside the panel.

## Building from source

```
dotnet build -c Release
```

Requires the Solar Expanse managed DLLs referenced in `PowerTracker.csproj`.

## Release

```
npm run release
```

Requires [GitHub CLI](https://cli.github.com/) (`gh`) to be authenticated.
