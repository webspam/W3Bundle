# Changelog

## Unreleased

- Paths keep their original case, as wcc_lite does. `metadata.store` hashes are still case-insensitive (ASCII only)
- Non-ASCII paths are stored as UTF-8 instead of `?`
- `metadatastore` directory table order now matches wcc_lite for nested trees
- `*.buffer` files are packed into `buffers0.bundle`
- `metadatastore` reads every `.bundle` in the directory
- `metadatastore` writes the buffer table, matching wcc_lite for `buffers0.bundle`
- `metadatastore` fails, like wcc_lite, on a buffer without an id or without a master file

## 0.1.0

- Initial release
- Adds wcc_lite compatible commands:
  - `pack`
  - `metadatastore`
- Adds a combination command to run both at once: `build`
- Byte-identical `blob0.bundle` and `metadata.store` output for the Light Rewrite repo
