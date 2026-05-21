# Changelog

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

## [1.1.0] - 2026-05-20
### Added
- Facility sub-rows and sub-header now have a dark background for clear visual separation from body rows.
- Transmission expand box uses a near-black background as a distinct third visual layer.
- Threshold panel info box has a **GOT IT** dismiss button — hides the notice permanently (saved to config).

### Changed
- Transmission expand box indent doubled to 32 px, aligning with the building icon in the facility row.
- Per-facility fuel rows in the threshold panel indented under their planet label (proper padding, removed text-space hack).
- Default global fuel thresholds: warning 2 years, critical 1 year (was 30 d / 7 d).

### Fixed
- Threshold panel info box now auto-sizes to fit its full text instead of clipping at a fixed height.

## [1.0.0] - 2026-05-20
### Added
- Power tracker panel (720×280 px, resizable) showing GEN/DAY, CONS/DAY, BALANCE, and BATTERY for each body.
- Expandable per-body facility sub-table: FACILITY, GEN/DAY, FUEL/DAY, WORKERS, SUPPLY, STOCK.
- Transmitter and receiver facilities appear in the facility sub-table with an expand toggle showing linked bodies, transfer ratios, and power values.
- Alert thresholds tab with per-body energy balance thresholds (warn %/crit %) and per-facility fuel supply thresholds (warn/crit in years+days).
- Global defaults with per-body/facility overrides and RST buttons.
- Severity colouring on BALANCE and SUPPLY columns (green/orange/red based on thresholds).
- TrackerUpdater component refreshes data every 5 seconds even while the panel is closed.
- POWER button positioned next to the notification button.
