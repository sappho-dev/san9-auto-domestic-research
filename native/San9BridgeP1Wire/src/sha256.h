#ifndef SAN9_P1_SHA256_H
#define SAN9_P1_SHA256_H

#include <stddef.h>
#include <stdint.h>

typedef struct San9P1Sha256Context {
    uint32_t state[8];
    uint64_t bit_count;
    uint8_t block[64];
    size_t block_used;
} San9P1Sha256Context;

void san9_p1_sha256_initialize(San9P1Sha256Context *context);
void san9_p1_sha256_update(San9P1Sha256Context *context, const uint8_t *data, size_t size);
void san9_p1_sha256_finish(San9P1Sha256Context *context, uint8_t output[32]);
void san9_p1_hmac_sha256(
    const uint8_t *key,
    size_t key_size,
    const uint8_t *data,
    size_t data_size,
    uint8_t output[32]);
void san9_p1_secure_zero(void *value, size_t size);

#endif
