# Changelog

## Unreleased

- Paths keep their original case, as wcc_lite does. `metadata.store` hashes are still case-insensitive (ASCII only)
- Non-ASCII paths are stored as UTF-8 instead of `?`

## 0.1.0

- Initial release
- Adds wcc_lite compatible commands:
  - `pack`
  - `metadatastore`
- Adds a combination command to run both at once: `build`
- Byte-identical `blob0.bundle` and `metadata.store` output for the Light Rewrite repo
