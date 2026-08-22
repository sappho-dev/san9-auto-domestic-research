#ifndef SAN9_SHA256_H
#define SAN9_SHA256_H

#include <stddef.h>
#include <stdint.h>

typedef struct San9Sha256Context {
    uint32_t state[8];
    uint64_t bit_count;
    uint8_t block[64];
    size_t block_used;
} San9Sha256Context;

void san9_sha256_initialize(San9Sha256Context *context);
void san9_sha256_update(San9Sha256Context *context, const uint8_t *data, size_t size);
void san9_sha256_finish(San9Sha256Context *context, uint8_t output[32]);
void san9_hmac_sha256(
    const uint8_t *key,
    size_t key_size,
    const uint8_t *data,
    size_t data_size,
    uint8_t output[32]);
int san9_constant_time_equal(const uint8_t *left, const uint8_t *right, size_t size);
void san9_secure_zero(void *value, size_t size);

#endif
