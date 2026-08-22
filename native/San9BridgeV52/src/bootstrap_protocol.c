#include "san9_bridge_v52.h"
#include "sha256.h"

#include <string.h>

_Static_assert(sizeof(San9V52BootstrapEnvelope) == SAN9_V52_ENVELOPE_SIZE,
    "V5.2 envelope size drift");
_Static_assert(offsetof(San9V52BootstrapEnvelope, session_nonce) == 112,
    "V5.2 envelope binding offset drift");
_Static_assert(offsetof(San9V52BootstrapEnvelope, bootstrap_mac) == 368,
    "V5.2 envelope MAC offset drift");
_Static_assert(sizeof(San9V52SharedBlock) == SAN9_V52_SHARED_SIZE,
    "V5.2 shared block size drift");
_Static_assert(offsetof(San9V52SharedBlock, envelope) == SAN9_V52_CONTROL_SIZE,
    "V5.2 envelope shared offset drift");
_Static_assert(offsetof(San9V52SharedBlock, mailbox) == 576,
    "V5.2 mailbox shared offset drift");

static int range_is_zero(const uint8_t *value, size_t size)
{
    uint8_t aggregate = 0;
    size_t index;
    for (index = 0; index < size; ++index) {
        aggregate = (uint8_t)(aggregate | value[index]);
    }
    return aggregate == 0;
}

static int envelope_has_required_values(const San9V52BootstrapEnvelope *envelope)
{
    return envelope->sequence != 0U
        && envelope->issued_at_ms != 0U
        && envelope->expires_at_ms > envelope->issued_at_ms
        && envelope->expires_at_ms - envelope->issued_at_ms
            <= SAN9_V52_MAX_BOOTSTRAP_LIFETIME_MS
        && envelope->controller_pid != 0U
        && envelope->target_pid != 0U
        && envelope->target_thread_id != 0U
        && envelope->target_hwnd != 0U
        && envelope->target_creation_time != 0U
        && envelope->controller_creation_time != 0U
        && envelope->registered_message >= 0xc000U
        && envelope->registered_message <= 0xffffU
        && envelope->mapping_atom != 0U
        && envelope->mapping_atom <= 0xffffU
        && envelope->message_tag != 0U
        && !range_is_zero(envelope->session_nonce, sizeof(envelope->session_nonce))
        && !range_is_zero(envelope->request_id, sizeof(envelope->request_id))
        && !range_is_zero(envelope->target_digest, sizeof(envelope->target_digest))
        && !range_is_zero(envelope->context_digest, sizeof(envelope->context_digest))
        && !range_is_zero(envelope->exe_digest, sizeof(envelope->exe_digest))
        && !range_is_zero(envelope->dll_digest, sizeof(envelope->dll_digest))
        && !range_is_zero(envelope->session_key, sizeof(envelope->session_key))
        && !range_is_zero(
            envelope->mapping_name_digest,
            sizeof(envelope->mapping_name_digest))
        && !range_is_zero(envelope->challenge, sizeof(envelope->challenge));
}

static void compute_bootstrap_mac(
    const San9V52BootstrapEnvelope *envelope,
    const uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE],
    uint8_t output[SAN9_PING_DIGEST_SIZE])
{
    San9V52BootstrapEnvelope canonical;
    memcpy(&canonical, envelope, sizeof(canonical));
    memset(canonical.bootstrap_mac, 0, sizeof(canonical.bootstrap_mac));
    san9_hmac_sha256(
        root_key,
        SAN9_V52_ROOT_KEY_SIZE,
        (const uint8_t *)&canonical,
        sizeof(canonical),
        output);
    san9_secure_zero(&canonical, sizeof(canonical));
}

int san9_v52_bootstrap_seal(
    San9V52BootstrapEnvelope *envelope,
    const uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE])
{
    uint8_t mac[SAN9_PING_DIGEST_SIZE];
    if (envelope == NULL || root_key == NULL
        || range_is_zero(root_key, SAN9_V52_ROOT_KEY_SIZE)) {
        return SAN9_V52_INVALID;
    }

    envelope->magic = SAN9_V52_BOOTSTRAP_MAGIC;
    envelope->schema_major = SAN9_V52_SCHEMA_MAJOR;
    envelope->schema_minor = SAN9_V52_SCHEMA_MINOR;
    envelope->structure_size = SAN9_V52_ENVELOPE_SIZE;
    envelope->flags = 0;
    envelope->exact_app_object = SAN9_EXACT_APP_OBJECT;
    envelope->exact_app_vtable = SAN9_EXACT_APP_VTABLE;
    envelope->exact_idle_slot = SAN9_EXACT_IDLE_SLOT;
    envelope->exact_original_idle = SAN9_EXACT_ORIGINAL_IDLE;
    envelope->exact_idle_caller = SAN9_EXACT_IDLE_CALLER;
    envelope->exact_scheduler_vtable = SAN9_EXACT_SCHEDULER_VTABLE;
    envelope->exact_controller_vtable = SAN9_EXACT_CONTROLLER_VTABLE;
    memset(envelope->bootstrap_mac, 0, sizeof(envelope->bootstrap_mac));

    if (!range_is_zero(envelope->reserved, sizeof(envelope->reserved))
        || !envelope_has_required_values(envelope)
        || !san9_constant_time_equal(
            envelope->target_digest,
            envelope->exe_digest,
            SAN9_PING_DIGEST_SIZE)) {
        return SAN9_V52_INVALID;
    }
    compute_bootstrap_mac(envelope, root_key, mac);
    memcpy(envelope->bootstrap_mac, mac, sizeof(mac));
    san9_secure_zero(mac, sizeof(mac));
    return SAN9_V52_OK;
}

int san9_v52_bootstrap_validate(
    const San9V52BootstrapEnvelope *envelope,
    const uint8_t root_key[SAN9_V52_ROOT_KEY_SIZE],
    const San9V52BootstrapExpectations *expected)
{
    uint8_t mac[SAN9_PING_DIGEST_SIZE];
    uint64_t next_sequence;

    if (envelope == NULL || root_key == NULL || expected == NULL
        || range_is_zero(root_key, SAN9_V52_ROOT_KEY_SIZE)
        || envelope->magic != SAN9_V52_BOOTSTRAP_MAGIC
        || envelope->schema_major != SAN9_V52_SCHEMA_MAJOR
        || envelope->schema_minor != SAN9_V52_SCHEMA_MINOR
        || envelope->structure_size != SAN9_V52_ENVELOPE_SIZE
        || envelope->flags != 0U
        || !range_is_zero(envelope->reserved, sizeof(envelope->reserved))
        || !envelope_has_required_values(envelope)
        || envelope->exact_app_object != SAN9_EXACT_APP_OBJECT
        || envelope->exact_app_vtable != SAN9_EXACT_APP_VTABLE
        || envelope->exact_idle_slot != SAN9_EXACT_IDLE_SLOT
        || envelope->exact_original_idle != SAN9_EXACT_ORIGINAL_IDLE
        || envelope->exact_idle_caller != SAN9_EXACT_IDLE_CALLER
        || envelope->exact_scheduler_vtable != SAN9_EXACT_SCHEDULER_VTABLE
        || envelope->exact_controller_vtable != SAN9_EXACT_CONTROLLER_VTABLE) {
        return SAN9_V52_INVALID;
    }

    compute_bootstrap_mac(envelope, root_key, mac);
    if (!san9_constant_time_equal(
            mac,
            envelope->bootstrap_mac,
            SAN9_PING_DIGEST_SIZE)) {
        san9_secure_zero(mac, sizeof(mac));
        return SAN9_V52_AUTH_FAILED;
    }
    san9_secure_zero(mac, sizeof(mac));

    if (expected->last_sequence == UINT64_MAX) {
        return SAN9_V52_SEQUENCE_FAILED;
    }
    next_sequence = expected->last_sequence + 1U;
    if (envelope->sequence != next_sequence || envelope->sequence != 1U) {
        return SAN9_V52_SEQUENCE_FAILED;
    }
    if (expected->now_ms < envelope->issued_at_ms
        || expected->now_ms >= envelope->expires_at_ms) {
        return SAN9_V52_EXPIRED;
    }

    if (envelope->controller_pid != expected->controller_pid
        || envelope->controller_creation_time != expected->controller_creation_time
        || envelope->target_pid != expected->target_pid
        || envelope->target_thread_id != expected->target_thread_id
        || envelope->target_hwnd != expected->target_hwnd
        || envelope->target_creation_time != expected->target_creation_time
        || envelope->registered_message != expected->registered_message
        || envelope->mapping_atom != expected->mapping_atom
        || envelope->message_tag != expected->message_tag
        || !san9_constant_time_equal(
            envelope->target_digest,
            expected->target_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            envelope->context_digest,
            expected->context_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            envelope->exe_digest,
            expected->exe_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            envelope->dll_digest,
            expected->dll_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            envelope->mapping_name_digest,
            expected->mapping_name_digest,
            SAN9_PING_DIGEST_SIZE)
        || !san9_constant_time_equal(
            envelope->target_digest,
            envelope->exe_digest,
            SAN9_PING_DIGEST_SIZE)) {
        return SAN9_V52_BINDING_FAILED;
    }
    return SAN9_V52_OK;
}
