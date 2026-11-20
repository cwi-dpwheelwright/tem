# CWI GPT Web UI Localization & Formatting

## Defaults
- Dates and times use the browser's default locale and time zone when no explicit locale is provided.
- Numbers use the browser's default locale with decimal formatting.

## Implementation
- `formatDateTime` relies on `Intl.DateTimeFormat` with `dateStyle: "medium"`, `timeStyle: "short"`, and `timeZoneName: "short"` to show the user's time zone.
- `formatNumber` relies on `Intl.NumberFormat` for locale-aware numeric output.

## Overrides
- Tests and callers can pass explicit locales and time zones to guarantee deterministic output.
