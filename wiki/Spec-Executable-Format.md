# AIL Executable Format

> Part of the [AIL Specification](Specification) — Version 2.1

---

## 8. Executable Format

An AIL executable (`.ila`) is a binary file composed of a header followed by one or more sections.

### File Header

| Offset | Size    | Field           | Description                      |
|--------|---------|-----------------|----------------------------------|
| 0      | 4 bytes | Magic           | `0x41 0x49 0x4C 0x00` (`AIL\0`) |
| 4      | 2 bytes | Version         | Format version (little-endian)   |
| 6      | 2 bytes | Section count   | Number of sections               |

### Section Entry

| Offset | Size    | Field           | Description                      |
|--------|---------|-----------------|----------------------------------|
| 0      | 2 bytes | Section type    | Type identifier                  |
| 2      | 4 bytes | Section length  | Length of section data in bytes  |
| 6      | N bytes | Section data    | Raw section content              |
