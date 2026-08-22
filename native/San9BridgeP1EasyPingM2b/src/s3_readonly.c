#include <string.h>

#include "s3_readonly.h"

#define RO UINT16_C(0x5333)

static const San9P1S3ReadDescriptor g_read_dispatch[] = {
    { SAN9_P1_S3_SYMBOL_APP,              0x0000u, 0x0008u, RO },
    { SAN9_P1_S3_SYMBOL_WINDOW,           0x001Cu, 0x0004u, RO },
    { SAN9_P1_S3_SYMBOL_OWNER,            0x0018u, 0x0004u, RO },
    { SAN9_P1_S3_SYMBOL_SCENE,            0x008Cu, 0x0004u, RO },
    { SAN9_P1_S3_SYMBOL_TASK,             0x0000u, 0x0014u, RO },
    { SAN9_P1_S3_SYMBOL_CONTROLLER,       0x0030u, 0x000Cu, RO },
    { SAN9_P1_S3_SYMBOL_HANDLER,          0x0000u, 0x0070u, RO },
    { SAN9_P1_S3_SYMBOL_OUTER,            0x0000u, 0x0020u, RO },
    { SAN9_P1_S3_SYMBOL_OUTER,            0x0680u, 0x0004u, RO },
    { SAN9_P1_S3_SYMBOL_OUTER,            0x06ACu, 0x0010u, RO },
    { SAN9_P1_S3_SYMBOL_OUTER,            0x06CCu, 0x0010u, RO },
    { SAN9_P1_S3_SYMBOL_OUTER,            0x06ECu, 0x0010u, RO },
    { SAN9_P1_S3_SYMBOL_SELECTOR,         0x0000u, 0x0020u, RO },
    { SAN9_P1_S3_SYMBOL_SELECTOR,         0x0154u, 0x0030u, RO },
    { SAN9_P1_S3_SYMBOL_COMMAND,          0x0000u, 0x0040u, RO },
    { SAN9_P1_S3_SYMBOL_GLOBAL_SELECTED,  0x0000u, 0x0010u, RO },
    { SAN9_P1_S3_SYMBOL_CURRENT_BUILDING, 0x0000u, 0x0004u, RO }
};

static const char g_s3_build_identity[] =
    "S3_READONLY_BUILD=1;READ_DISPATCH=17;WRITE_DISPATCH=0;MAX_READ=256";

_Static_assert(sizeof(g_read_dispatch) / sizeof(g_read_dispatch[0])
        == SAN9_P1_S3_READ_OPCODE_COUNT,
    "S3 read dispatch count changed");

uint32_t san9_p1_s3_read_dispatch_count(void)
{
    return (uint32_t)(sizeof(g_read_dispatch) / sizeof(g_read_dispatch[0]));
}

uint32_t san9_p1_s3_write_dispatch_count(void)
{
    return SAN9_P1_S3_WRITE_DISPATCH_COUNT;
}

const char *san9_p1_s3_build_identity(void)
{
    return g_s3_build_identity;
}

const San9P1S3ReadDescriptor *san9_p1_s3_read_descriptor(uint32_t index)
{
    return index < san9_p1_s3_read_dispatch_count()
        ? &g_read_dispatch[index] : NULL;
}

int san9_p1_s3_read_index(
    const San9P1S3ReadContext *context,
    uint32_t index,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_size)
{
    const San9P1S3ReadDescriptor *descriptor;
    uint32_t base;
    uint32_t address;
    if (output_size != NULL) {
        *output_size = 0u;
    }
    if (context == NULL || output == NULL || output_size == NULL
        || context->read == NULL) {
        return 0;
    }
    descriptor = san9_p1_s3_read_descriptor(index);
    if (descriptor == NULL || descriptor->readonly_marker != RO
        || descriptor->symbol >= SAN9_P1_S3_SYMBOL_COUNT
        || descriptor->length == 0u || descriptor->length > SAN9_P1_S3_READ_MAX
        || descriptor->length > output_capacity) {
        return 0;
    }
    base = context->bases[descriptor->symbol];
    if (base == 0u || base > UINT32_MAX - descriptor->offset) {
        return 0;
    }
    address = base + descriptor->offset;
    if (address > UINT32_MAX - descriptor->length
        || !context->read(context->read_context, address, output,
            descriptor->length)) {
        memset(output, 0, descriptor->length);
        return 0;
    }
    *output_size = descriptor->length;
    return 1;
}
