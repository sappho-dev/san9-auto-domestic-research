#include <stddef.h>
#include <string.h>

#include "san9_p1_m2b.h"
#include "sha256.h"

static const uint8_t g_s5_evidence_domain[] =
    "SAN9-S5-NO-APPLY-EVIDENCE-v1";
static const uint8_t g_s5_modal_evidence_domain[] =
    "SAN9-S5-MODAL-PROBE-EVIDENCE-v1";

static int key_exact(const uint8_t *key, size_t size)
{
    uint8_t any = 0u;
    size_t i;
    if (key == NULL || size != SAN9_S5_HMAC_KEY_SIZE) {
        return 0;
    }
    for (i = 0u; i < size; ++i) {
        any |= key[i];
    }
    return any != 0u;
}

static void evidence_hmac(
    const San9P1S5NoApplyEvidence *evidence,
    const uint8_t *key,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_s5_evidence_domain) - 1u)
        + sizeof(San9P1S5NoApplyEvidence)];
    size_t prefix = sizeof(g_s5_evidence_domain) - 1u;
    memcpy(material, g_s5_evidence_domain, prefix);
    memcpy(material + prefix, evidence, sizeof(*evidence));
    memset(material + prefix + offsetof(San9P1S5NoApplyEvidence, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

int san9_p1_s5_evidence_sign(
    San9P1S5NoApplyEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    if (evidence == NULL || !key_exact(hmac_key, hmac_key_size)) {
        return 0;
    }
    evidence_hmac(evidence, hmac_key, digest);
    memcpy(evidence->hmac, digest, sizeof(evidence->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return 1;
}

int san9_p1_s5_evidence_verify(
    const San9P1S5NoApplyEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    int valid;
    if (evidence == NULL || !key_exact(hmac_key, hmac_key_size)) {
        return 0;
    }
    evidence_hmac(evidence, hmac_key, digest);
    valid = san9_p1_constant_time_equal(
        evidence->hmac, digest, sizeof(digest));
    san9_p1_secure_zero(digest, sizeof(digest));
    return valid;
}

static void modal_evidence_hmac(
    const San9P1S5ModalProbeEvidence *evidence,
    const uint8_t *key,
    uint8_t output[SAN9_S5_DIGEST_SIZE])
{
    uint8_t material[(sizeof(g_s5_modal_evidence_domain) - 1u)
        + sizeof(San9P1S5ModalProbeEvidence)];
    size_t prefix = sizeof(g_s5_modal_evidence_domain) - 1u;
    memcpy(material, g_s5_modal_evidence_domain, prefix);
    memcpy(material + prefix, evidence, sizeof(*evidence));
    memset(material + prefix + offsetof(San9P1S5ModalProbeEvidence, hmac),
        0, SAN9_S5_DIGEST_SIZE);
    san9_p1_hmac_sha256(key, SAN9_S5_HMAC_KEY_SIZE,
        material, sizeof(material), output);
    san9_p1_secure_zero(material, sizeof(material));
}

int san9_p1_s5_modal_evidence_sign(
    San9P1S5ModalProbeEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    if (evidence == NULL || !key_exact(hmac_key, hmac_key_size)) {
        return 0;
    }
    modal_evidence_hmac(evidence, hmac_key, digest);
    memcpy(evidence->hmac, digest, sizeof(evidence->hmac));
    san9_p1_secure_zero(digest, sizeof(digest));
    return 1;
}

int san9_p1_s5_modal_evidence_verify(
    const San9P1S5ModalProbeEvidence *evidence,
    const uint8_t *hmac_key,
    size_t hmac_key_size)
{
    uint8_t digest[SAN9_S5_DIGEST_SIZE];
    int valid;
    if (evidence == NULL || !key_exact(hmac_key, hmac_key_size)) {
        return 0;
    }
    modal_evidence_hmac(evidence, hmac_key, digest);
    valid = san9_p1_constant_time_equal(
        evidence->hmac, digest, sizeof(digest));
    san9_p1_secure_zero(digest, sizeof(digest));
    return valid;
}

int san9_p1_s5_modal_evidence_is_vtable_exact(
    const San9P1S5ModalProbeEvidence *evidence,
    uint32_t expected_tid,
    uint32_t expected_message,
    uint32_t expected_atom,
    uint32_t expected_challenge)
{
    uint8_t digest_any = 0u;
    uint32_t reserved_any = 0u;
    size_t index;
    if (evidence == NULL || expected_tid == 0u || expected_message < 0xC000u
        || expected_atom == 0u || expected_challenge == 0u) {
        return 0;
    }
    for (index = 0u; index < SAN9_P1_DIGEST_SIZE; ++index) {
        digest_any |= evidence->arm_easy_digest[index];
    }
    for (index = 0u;
            index < sizeof(evidence->reserved) / sizeof(evidence->reserved[0]);
            ++index) {
        reserved_any |= evidence->reserved[index];
    }
    return evidence->magic == SAN9_P1_S5_MODAL_EVIDENCE_MAGIC
        && evidence->schema_major == SAN9_P1_M2B_SCHEMA_MAJOR
        && evidence->schema_minor == SAN9_P1_M2B_SCHEMA_MINOR
        && evidence->structure_size == sizeof(*evidence)
        && evidence->terminal_code == SAN9_P1_M2B_OK
        && evidence->restart_required == 0u
        && evidence->state == SAN9_P1_S5_MODAL_COMPLETE
        && evidence->wake_post_count == 1u
        && evidence->hook_entry_count == 1u
        && evidence->exact_wake_count == 1u
        && evidence->wake_claim_count == 1u
        && evidence->duplicate_count == 0u
        && evidence->hook_depth == 1u
        && evidence->hook_reentry_count == 0u
        && evidence->hook_code == 0u
        && evidence->remove_flag == 1u
        && evidence->message == expected_message
        && evidence->atom == expected_atom
        && evidence->challenge == expected_challenge
        && evidence->callnext_stable == 1u
        && evidence->shadow_arm_count == 1u
        && evidence->wrapper_enter_count == SAN9_P1_M2B_S5_MODAL_PROBE_TICKS
        && evidence->original_return_count == SAN9_P1_M2B_S5_MODAL_PROBE_TICKS
        && evidence->restore_count == 1u
        && evidence->identity_reject_count == 0u
        && evidence->wrapper_reentry_count == 0u
        && evidence->first_tid == expected_tid
        && evidence->last_tid == expected_tid
        && evidence->last_caller == SAN9_P1_M2B_DOMESTIC_MENU_TICK_CALLER
        && evidence->message_hwnd == 0u
        && evidence->top_hwnd != 0u
        && evidence->menu_vptr == SAN9_P1_M2B_DOMESTIC_MENU_VPTR
        && (evidence->modal_flags & 0x10u) != 0u
        && evidence->easy_stable_count == SAN9_P1_M2B_S5_MODAL_PROBE_TICKS
        && evidence->controller_ack == 0u
        && evidence->menu_pointer >= UINT32_C(0x00010000)
        && (evidence->menu_pointer & 3u) == 0u
        && digest_any != 0u && reserved_any == 0u
        && san9_p1_constant_time_equal(evidence->arm_easy_digest,
            evidence->last_easy_digest, SAN9_P1_DIGEST_SIZE);
}
