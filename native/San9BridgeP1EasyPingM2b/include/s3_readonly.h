#ifndef SAN9_P1_M2B_S3_READONLY_H
#define SAN9_P1_M2B_S3_READONLY_H

#include <stddef.h>
#include <stdint.h>

#if !defined(S3_READONLY_BUILD) || S3_READONLY_BUILD != 1
#error "S3 observation code requires S3_READONLY_BUILD=1"
#endif

#define SAN9_P1_S3_READONLY_MARKER UINT32_C(0x524F5333)
#define SAN9_P1_S3_READ_MAX 256u
#define SAN9_P1_S3_WRITE_DISPATCH_COUNT 0u

typedef enum San9P1S3Symbol {
    SAN9_P1_S3_SYMBOL_APP = 0,
    SAN9_P1_S3_SYMBOL_WINDOW = 1,
    SAN9_P1_S3_SYMBOL_OWNER = 2,
    SAN9_P1_S3_SYMBOL_SCENE = 3,
    SAN9_P1_S3_SYMBOL_TASK = 4,
    SAN9_P1_S3_SYMBOL_CONTROLLER = 5,
    SAN9_P1_S3_SYMBOL_HANDLER = 6,
    SAN9_P1_S3_SYMBOL_OUTER = 7,
    SAN9_P1_S3_SYMBOL_SELECTOR = 8,
    SAN9_P1_S3_SYMBOL_COMMAND = 9,
    SAN9_P1_S3_SYMBOL_GLOBAL_SELECTED = 10,
    SAN9_P1_S3_SYMBOL_CURRENT_BUILDING = 11,
    SAN9_P1_S3_SYMBOL_COUNT = 12
} San9P1S3Symbol;

typedef enum San9P1S3ReadOpcode {
    SAN9_P1_S3_READ_APP_HEADER = 0,
    SAN9_P1_S3_READ_WINDOW_OWNER = 1,
    SAN9_P1_S3_READ_OWNER_SCENE = 2,
    SAN9_P1_S3_READ_SCENE_ROOT = 3,
    SAN9_P1_S3_READ_TASK_HEADER = 4,
    SAN9_P1_S3_READ_CONTROLLER_FIELDS = 5,
    SAN9_P1_S3_READ_HANDLER_PREFIX = 6,
    SAN9_P1_S3_READ_OUTER_PREFIX = 7,
    SAN9_P1_S3_READ_OUTER_RESULT = 8,
    SAN9_P1_S3_READ_OUTER_COMMITTED = 9,
    SAN9_P1_S3_READ_OUTER_SOURCE = 10,
    SAN9_P1_S3_READ_OUTER_WORKING = 11,
    SAN9_P1_S3_READ_SELECTOR_PREFIX = 12,
    SAN9_P1_S3_READ_SELECTOR_SELECTION = 13,
    SAN9_P1_S3_READ_COMMAND_PREFIX = 14,
    SAN9_P1_S3_READ_GLOBAL_SELECTED = 15,
    SAN9_P1_S3_READ_CURRENT_BUILDING = 16,
    SAN9_P1_S3_READ_OPCODE_COUNT = 17
} San9P1S3ReadOpcode;

typedef struct San9P1S3ReadDescriptor {
    uint16_t symbol;
    uint16_t offset;
    uint16_t length;
    uint16_t readonly_marker;
} San9P1S3ReadDescriptor;

typedef int (*San9P1S3ReadCallback)(
    void *context,
    uint32_t address,
    uint8_t *output,
    size_t output_size);

typedef struct San9P1S3ReadContext {
    uint32_t bases[SAN9_P1_S3_SYMBOL_COUNT];
    San9P1S3ReadCallback read;
    void *read_context;
} San9P1S3ReadContext;

uint32_t san9_p1_s3_read_dispatch_count(void);
uint32_t san9_p1_s3_write_dispatch_count(void);
const char *san9_p1_s3_build_identity(void);
const San9P1S3ReadDescriptor *san9_p1_s3_read_descriptor(uint32_t index);
int san9_p1_s3_read_index(
    const San9P1S3ReadContext *context,
    uint32_t index,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_size);

#endif
