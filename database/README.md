# LeadMine data directory

This is the application's datastore. It is created automatically on first boot, so a
fresh clone needs no setup.

| File                  | Purpose                                          | Tracked in git |
| --------------------- | ------------------------------------------------ | -------------- |
| `Businesses.xlsx`     | Primary lead datastore (sheet: `Businesses`)      | **Yes**        |
| `logs.jsonl`          | Append-only activity log                          | Yes            |
| `search-history.json` | Past search runs                                  | Yes            |
| `settings.json`       | API key, rate limits, crawler configuration       | No — secret    |
| `exports/`            | Generated `.xlsx` / `.csv` exports                | No (folder kept) |

`settings.json` stores the Google Places API key in plaintext and is excluded by
`.gitignore`. Keep it that way — use the `GOOGLE_PLACES_API_KEY` environment variable in
production instead.

> `logs.jsonl` is append-only and grows with every run, so it produces a large diff on
> each commit. If that becomes noisy, untrack it with
> `git rm --cached database/logs.jsonl` and add it to `.gitignore`.

## Editing the workbook by hand

Safe to do, with two rules:

- **Stop the app first.** It caches the sheet in memory and rewrites the whole file on
  every save, so concurrent edits will be overwritten.
- **Don't rename the header row.** Rows are matched by header *name*, not position, so
  reordering columns is fine but renaming them is not.

## Relocating the data

Point `LEADMINE_DATA_DIR` at any writable path — use this to mount a persistent volume in
a container deployment:

```bash
LEADMINE_DATA_DIR=/data npm start
```
