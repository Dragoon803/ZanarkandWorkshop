# Treasure and Gear Hex Pattern Analysis

Sources examined:

- `takara.bin.hexpat`
- `buki_get.bin.hexpat`
- clean retail `takara.bin` and `buki_get.bin`

## Shared header

Both files use the same indexed-table header convention:

- `u16 max_idx` at `0x0A` (record count is `max_idx + 1`)
- `u32 start_offset` at `0x10`
- retail `start_offset` is `0x14`

Retail values are 498 treasure records and 86 gear-reward records. The current treasure reader's fixed `0x14` start is correct for these files, but reading the declared offset is the more robust implementation.

## `takara.bin` records

Each record is exactly four bytes:

| Offset | Field | Meaning |
|---:|---|---|
| `+0` | `u8 type` | `0` gil, `2` item, `5` gear, `10` key item |
| `+1` | `u8 amount` | Quantity; gil uses units of 100 |
| `+2` | `u16 item_id` | Category-specific identifier |

The 16-bit ID must be interpreted using the type:

- Gil: ID is `0`; display `amount × 100 Gil`.
- Item: IDs are `0x2000` through `0x206F`; subtract `0x2000` to obtain the existing `Item_Dictionary` index `0..111`.
- Key item: IDs begin at `0xA000`; subtract `0xA000` to obtain the key-item index. Gaps are valid.
- Gear: IDs are `1..85` and select a record in `buki_get.bin` directly.

Clean-file distribution confirms 28 gil, 362 item, 82 gear, and 26 key-item rewards. Every normal-item ID falls within the known 112-item range.

## `buki_get.bin` records

Each gear reward is 16 bytes, not the 22-byte inventory `EquipmentStruct` used elsewhere in the project:

| Offset | Field |
|---:|---|
| `+0` | flags |
| `+1` | owner/character |
| `+2` | weapon (`0`) or armor (`1`) |
| `+3` | padding |
| `+4` | damage formula |
| `+5` | power |
| `+6` | critical bonus |
| `+7` | slot count |
| `+8` | ability 1 (`u16`) |
| `+A` | ability 2 (`u16`) |
| `+C` | ability 3 (`u16`) |
| `+E` | ability 4 (`u16`) |

Ability IDs use the `0x8000` prefix and correspond to the existing auto-ability dictionary after subtracting `0x8000`. Empty ability slots appear as `0x00FF` in retail data.

The owner and damage-formula values align with the existing `Character_Enum` and `DamageFormula_Enum`. The flags align with the existing equipment flags: bit `0x02` hidden, `0x04` celestial, and `0x08` Brotherhood. The pattern labels bit `0x01` as padding, while the project currently calls it `IsSummon`; that bit should remain raw/preserved until separately confirmed.

## Confirmed pattern imperfections

- The `takara` item enum begins named gear selectors at `Buki_Get_2`, but retail treasure data uses gear selector `1`; the enum is incomplete at its lower boundary.
- The first `buki_get` flag bit is labeled padding, conflicting with the existing inventory interpretation. It should not be presented as a settled user-facing property yet.
- `buki_get` contains a reward template, not a localized equipment name ID. Friendly gear choices should therefore be generated descriptions such as `Tidus Weapon — Firestrike, Sensor` unless another naming source is joined.

## Recommended editor translation

Replace raw treasure controls with a type selector and a type-dependent reward selector:

- Gil: numeric amount displayed in actual gil, stored divided by 100.
- Item: searchable dropdown from `Item_Dictionary`, storing `0x2000 + index`.
- Key item: searchable key-item dropdown, storing `0xA000 + index`.
- Gear: searchable `buki_get` template dropdown summarized by owner, weapon/armor, slots, and abilities; optionally show its detailed formula/power/critical fields read-only.

Retain an advanced raw-ID display for provenance and unsupported/modded values. Never normalize an unknown value merely by opening and saving the editor.
