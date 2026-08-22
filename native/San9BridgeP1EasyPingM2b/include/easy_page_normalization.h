#ifndef SAN9_P1_M2B_EASY_PAGE_NORMALIZATION_H
#define SAN9_P1_M2B_EASY_PAGE_NORMALIZATION_H

#include <stdint.h>

/* VirtualProtect requests PAGE_READWRITE for Easy's table page.  Because the
   page belongs to the exact San9PK image section, Windows reports the live
   view as PAGE_WRITECOPY.  Canonicalization is allowed only for this exact
   profile; every field is checked so other mappings remain unmodified. */
#define SAN9_P1_M2B_EXACT_GAME_IMAGE_BASE UINT32_C(0x00400000)
#define SAN9_P1_M2B_EASY_TABLE_PAGE UINT32_C(0x0061C000)
#define SAN9_P1_M2B_EASY_TABLE_PAGE_SIZE UINT32_C(0x00001000)
#define SAN9_P1_M2B_MEM_COMMIT UINT32_C(0x00001000)
#define SAN9_P1_M2B_MEM_IMAGE UINT32_C(0x01000000)
#define SAN9_P1_M2B_PAGE_READWRITE UINT32_C(0x00000004)
#define SAN9_P1_M2B_PAGE_WRITECOPY UINT32_C(0x00000008)
#define SAN9_P1_M2B_PAGE_EXECUTE_WRITECOPY UINT32_C(0x00000080)

static inline uint32_t san9_p1_m2b_canonical_easy_page_protection(
    uint32_t address,
    uint32_t region_base,
    uint32_t region_size,
    uint32_t allocation_base,
    uint32_t allocation_protection,
    uint32_t state,
    uint32_t type,
    uint32_t observed_protection)
{
    uint64_t region_end = (uint64_t)region_base + (uint64_t)region_size;
    uint64_t page_end = (uint64_t)SAN9_P1_M2B_EASY_TABLE_PAGE
        + (uint64_t)SAN9_P1_M2B_EASY_TABLE_PAGE_SIZE;
    if (address == SAN9_P1_M2B_EASY_TABLE_PAGE
        && region_base == SAN9_P1_M2B_EASY_TABLE_PAGE
        && region_end >= page_end
        && region_end <= UINT64_C(0x100000000)
        && allocation_base == SAN9_P1_M2B_EXACT_GAME_IMAGE_BASE
        && allocation_protection == SAN9_P1_M2B_PAGE_EXECUTE_WRITECOPY
        && state == SAN9_P1_M2B_MEM_COMMIT
        && type == SAN9_P1_M2B_MEM_IMAGE
        && observed_protection == SAN9_P1_M2B_PAGE_WRITECOPY) {
        return SAN9_P1_M2B_PAGE_READWRITE;
    }
    return observed_protection;
}

#endif
