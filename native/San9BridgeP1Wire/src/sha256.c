#include "sha256.h"

#include <string.h>

static const uint32_t ROUND_CONSTANTS[64] = {
    0x428a2f98U, 0x71374491U, 0xb5c0fbcfU, 0xe9b5dba5U,
    0x3956c25bU, 0x59f111f1U, 0x923f82a4U, 0xab1c5ed5U,
    0xd807aa98U, 0x12835b01U, 0x243185beU, 0x550c7dc3U,
    0x72be5d74U, 0x80deb1feU, 0x9bdc06a7U, 0xc19bf174U,
    0xe49b69c1U, 0xefbe4786U, 0x0fc19dc6U, 0x240ca1ccU,
    0x2de92c6fU, 0x4a7484aaU, 0x5cb0a9dcU, 0x76f988daU,
    0x983e5152U, 0xa831c66dU, 0xb00327c8U, 0xbf597fc7U,
    0xc6e00bf3U, 0xd5a79147U, 0x06ca6351U, 0x14292967U,
    0x27b70a85U, 0x2e1b2138U, 0x4d2c6dfcU, 0x53380d13U,
    0x650a7354U, 0x766a0abbU, 0x81c2c92eU, 0x92722c85U,
    0xa2bfe8a1U, 0xa81a664bU, 0xc24b8b70U, 0xc76c51a3U,
    0xd192e819U, 0xd6990624U, 0xf40e3585U, 0x106aa070U,
    0x19a4c116U, 0x1e376c08U, 0x2748774cU, 0x34b0bcb5U,
    0x391c0cb3U, 0x4ed8aa4aU, 0x5b9cca4fU, 0x682e6ff3U,
    0x748f82eeU, 0x78a5636fU, 0x84c87814U, 0x8cc70208U,
    0x90befffaU, 0xa4506cebU, 0xbef9a3f7U, 0xc67178f2U
};

static uint32_t rotate_right(uint32_t value, unsigned count)
{
    return (value >> count) | (value << (32U - count));
}

static uint32_t read_big_u32(const uint8_t *value)
{
    return ((uint32_t)value[0] << 24)
        | ((uint32_t)value[1] << 16)
        | ((uint32_t)value[2] << 8)
        | (uint32_t)value[3];
}

static void write_big_u32(uint8_t *output, uint32_t value)
{
    output[0] = (uint8_t)(value >> 24);
    output[1] = (uint8_t)(value >> 16);
    output[2] = (uint8_t)(value >> 8);
    output[3] = (uint8_t)value;
}

static void transform(San9P1Sha256Context *context, const uint8_t block[64])
{
    uint32_t words[64];
    uint32_t a;
    uint32_t b;
    uint32_t c;
    uint32_t d;
    uint32_t e;
    uint32_t f;
    uint32_t g;
    uint32_t h;
    unsigned index;

    for (index = 0; index < 16; ++index) {
        words[index] = read_big_u32(block + (index * 4U));
    }
    for (index = 16; index < 64; ++index) {
        uint32_t first = rotate_right(words[index - 15], 7)
            ^ rotate_right(words[index - 15], 18)
            ^ (words[index - 15] >> 3);
        uint32_t second = rotate_right(words[index - 2], 17)
            ^ rotate_right(words[index - 2], 19)
            ^ (words[index - 2] >> 10);
        words[index] = words[index - 16] + first + words[index - 7] + second;
    }

    a = context->state[0];
    b = context->state[1];
    c = context->state[2];
    d = context->state[3];
    e = context->state[4];
    f = context->state[5];
    g = context->state[6];
    h = context->state[7];
    for (index = 0; index < 64; ++index) {
        uint32_t sigma_one = rotate_right(e, 6) ^ rotate_right(e, 11) ^ rotate_right(e, 25);
        uint32_t choose = (e & f) ^ ((~e) & g);
        uint32_t temporary_one = h + sigma_one + choose + ROUND_CONSTANTS[index] + words[index];
        uint32_t sigma_zero = rotate_right(a, 2) ^ rotate_right(a, 13) ^ rotate_right(a, 22);
        uint32_t majority = (a & b) ^ (a & c) ^ (b & c);
        uint32_t temporary_two = sigma_zero + majority;
        h = g;
        g = f;
        f = e;
        e = d + temporary_one;
        d = c;
        c = b;
        b = a;
        a = temporary_one + temporary_two;
    }

    context->state[0] += a;
    context->state[1] += b;
    context->state[2] += c;
    context->state[3] += d;
    context->state[4] += e;
    context->state[5] += f;
    context->state[6] += g;
    context->state[7] += h;
    san9_p1_secure_zero(words, sizeof(words));
}

void san9_p1_sha256_initialize(San9P1Sha256Context *context)
{
    memset(context, 0, sizeof(*context));
    context->state[0] = 0x6a09e667U;
    context->state[1] = 0xbb67ae85U;
    context->state[2] = 0x3c6ef372U;
    context->state[3] = 0xa54ff53aU;
    context->state[4] = 0x510e527fU;
    context->state[5] = 0x9b05688cU;
    context->state[6] = 0x1f83d9abU;
    context->state[7] = 0x5be0cd19U;
}

void san9_p1_sha256_update(San9P1Sha256Context *context, const uint8_t *data, size_t size)
{
    size_t consumed = 0;
    if (size == 0U) {
        return;
    }
    context->bit_count += (uint64_t)size * 8U;
    while (consumed < size) {
        size_t available = 64U - context->block_used;
        size_t remaining = size - consumed;
        size_t take = remaining < available ? remaining : available;
        memcpy(context->block + context->block_used, data + consumed, take);
        context->block_used += take;
        consumed += take;
        if (context->block_used == 64U) {
            transform(context, context->block);
            context->block_used = 0U;
        }
    }
}

void san9_p1_sha256_finish(San9P1Sha256Context *context, uint8_t output[32])
{
    uint8_t length_bytes[8];
    unsigned index;
    uint64_t bit_count = context->bit_count;

    context->block[context->block_used++] = 0x80U;
    if (context->block_used > 56U) {
        memset(context->block + context->block_used, 0, 64U - context->block_used);
        transform(context, context->block);
        context->block_used = 0U;
    }
    memset(context->block + context->block_used, 0, 56U - context->block_used);
    for (index = 0; index < 8; ++index) {
        length_bytes[7U - index] = (uint8_t)(bit_count >> (index * 8U));
    }
    memcpy(context->block + 56U, length_bytes, sizeof(length_bytes));
    transform(context, context->block);
    for (index = 0; index < 8; ++index) {
        write_big_u32(output + (index * 4U), context->state[index]);
    }
    san9_p1_secure_zero(length_bytes, sizeof(length_bytes));
    san9_p1_secure_zero(context, sizeof(*context));
}

void san9_p1_hmac_sha256(
    const uint8_t *key,
    size_t key_size,
    const uint8_t *data,
    size_t data_size,
    uint8_t output[32])
{
    uint8_t normalized[64];
    uint8_t inner_pad[64];
    uint8_t outer_pad[64];
    uint8_t inner_digest[32];
    San9P1Sha256Context context;
    size_t index;

    memset(normalized, 0, sizeof(normalized));
    if (key_size > sizeof(normalized)) {
        san9_p1_sha256_initialize(&context);
        san9_p1_sha256_update(&context, key, key_size);
        san9_p1_sha256_finish(&context, normalized);
    } else {
        memcpy(normalized, key, key_size);
    }
    for (index = 0; index < sizeof(normalized); ++index) {
        inner_pad[index] = (uint8_t)(normalized[index] ^ 0x36U);
        outer_pad[index] = (uint8_t)(normalized[index] ^ 0x5cU);
    }

    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, inner_pad, sizeof(inner_pad));
    san9_p1_sha256_update(&context, data, data_size);
    san9_p1_sha256_finish(&context, inner_digest);
    san9_p1_sha256_initialize(&context);
    san9_p1_sha256_update(&context, outer_pad, sizeof(outer_pad));
    san9_p1_sha256_update(&context, inner_digest, sizeof(inner_digest));
    san9_p1_sha256_finish(&context, output);

    san9_p1_secure_zero(normalized, sizeof(normalized));
    san9_p1_secure_zero(inner_pad, sizeof(inner_pad));
    san9_p1_secure_zero(outer_pad, sizeof(outer_pad));
    san9_p1_secure_zero(inner_digest, sizeof(inner_digest));
}

void san9_p1_secure_zero(void *value, size_t size)
{
    volatile uint8_t *bytes = (volatile uint8_t *)value;
    while (size != 0U) {
        *bytes++ = 0U;
        --size;
    }
}
