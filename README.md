# W3Bundle

Fast replacement for `wcc_lite.exe pack` and `wcc_lite.exe metadatastore` (Witcher 3 mod bundles).

wcc_lite spends ~9 s on engine startup before packing. W3Bundle packs and writes `metadata.store` in ~30 ms per bundle (NativeAOT).

## Usage

```sh
W3Bundle pack -dir=<source> -outdir=<content>
W3Bundle metadatastore -path=<content>
W3Bundle build -dir=<source> -outdir=<content>
```

- Argument syntax matches wcc_lite; `-agreetoterms` is accepted and ignored
- `build`: `pack` then `metadatastore` in one process

## Build

```sh
dotnet publish
```

## Test

```sh
dotnet test
```

## Compatibility with wcc_lite

`blob0.bundle` and `metadata.store` matched wcc_lite byte-for-byte in all tests so far. See [How W3Bundle works](docs/how-it-works.md).
