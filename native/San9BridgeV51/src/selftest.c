#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include "san9_bridge_v51.h"
#include "sha256.h"

#include <string.h>

typedef struct TestContext {
    San9BridgeSelfTestReport *report;
} TestContext;

static void check(TestContext *context, int condition)
{
    if (condition) {
        ++context->report->passed;
    } else {
        ++context->report->failed;
    }
}

static void fill_bytes(uint8_t *output, size_t size, uint8_t seed)
{
    size_t index;
    for (index = 0; index < size; ++index) {
        output[index] = (uint8_t)(seed + (uint8_t)(index * 17U));
    }
}

static int initialize_fixture(
    San9PingSession *session,
    San9PingMailbox *mailbox,
    San9IdleGateSnapshot *gate,
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE],
    uint8_t challenge[SAN9_PING_DIGEST_SIZE])
{
    uint8_t key[SAN9_PING_KEY_SIZE];
    uint8_t nonce[SAN9_PING_NONCE_SIZE];
    uint8_t target[SAN9_PING_DIGEST_SIZE];
    uint8_t context[SAN9_PING_DIGEST_SIZE];
    const uint32_t thread_id = 0x31415926U;
    const uint32_t idle_bridge = 0x13572468U;

    fill_bytes(key, sizeof(key), 0x11U);
    fill_bytes(nonce, sizeof(nonce), 0x22U);
    fill_bytes(target, sizeof(target), 0x33U);
    fill_bytes(context, sizeof(context), 0x44U);
    fill_bytes(request_id, SAN9_PING_REQUEST_ID_SIZE, 0x55U);
    fill_bytes(challenge, SAN9_PING_DIGEST_SIZE, 0x66U);

    san9_ping_mailbox_initialize(mailbox);
    memset(gate, 0, sizeof(*gate));
    gate->caller = SAN9_EXACT_IDLE_CALLER;
    gate->app_object = SAN9_EXACT_APP_OBJECT;
    gate->thread_id = thread_id;
    gate->slot_address = SAN9_EXACT_IDLE_SLOT;
    gate->slot_value = idle_bridge;
    gate->app_vptr = SAN9_EXACT_APP_VTABLE;
    gate->idle_argument = 0;
    gate->exact_target_verified = 1;
    gate->conflict_free = 1;
    gate->process_generation_stable = 1;
    return san9_ping_session_initialize(
        session,
        key,
        nonce,
        target,
        context,
        thread_id,
        idle_bridge);
}

static void test_hashes(TestContext *test)
{
    static const uint8_t expected_sha256[32] = {
        0xba, 0x78, 0x16, 0xbf, 0x8f, 0x01, 0xcf, 0xea,
        0x41, 0x41, 0x40, 0xde, 0x5d, 0xae, 0x22, 0x23,
        0xb0, 0x03, 0x61, 0xa3, 0x96, 0x17, 0x7a, 0x9c,
        0xb4, 0x10, 0xff, 0x61, 0xf2, 0x00, 0x15, 0xad
    };
    static const uint8_t expected_hmac[32] = {
        0xb0, 0x34, 0x4c, 0x61, 0xd8, 0xdb, 0x38, 0x53,
        0x5c, 0xa8, 0xaf, 0xce, 0xaf, 0x0b, 0xf1, 0x2b,
        0x88, 0x1d, 0xc2, 0x00, 0xc9, 0x83, 0x3d, 0xa7,
        0x26, 0xe9, 0x37, 0x6c, 0x2e, 0x32, 0xcf, 0xf7
    };
    static const uint8_t abc[] = { 'a', 'b', 'c' };
    static const uint8_t hi_there[] = {
        'H', 'i', ' ', 'T', 'h', 'e', 'r', 'e'
    };
    San9Sha256Context sha;
    uint8_t digest[32];
    uint8_t key[20];

    memset(key, 0x0b, sizeof(key));
    san9_sha256_initialize(&sha);
    san9_sha256_update(&sha, abc, sizeof(abc));
    san9_sha256_finish(&sha, digest);
    check(test, san9_constant_time_equal(digest, expected_sha256, sizeof(digest)));

    san9_hmac_sha256(key, sizeof(key), hi_there, sizeof(hi_there), digest);
    check(test, san9_constant_time_equal(digest, expected_hmac, sizeof(digest)));
    digest[0] ^= 1U;
    check(test, !san9_constant_time_equal(digest, expected_hmac, sizeof(digest)));
    san9_secure_zero(digest, sizeof(digest));
}

static void test_gate_matrix(TestContext *test)
{
    San9PingSession session;
    San9PingMailbox mailbox;
    San9IdleGateSnapshot gate;
    San9IdleGateSnapshot changed;
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE];
    uint8_t challenge[SAN9_PING_DIGEST_SIZE];

    check(test, initialize_fixture(&session, &mailbox, &gate, request_id, challenge) == SAN9_PING_OK);
    check(test, san9_idle_gate_validate(&session, &gate));

#define REJECT_CHANGED(field, value) \
    do { \
        changed = gate; \
        changed.field = (value); \
        check(test, !san9_idle_gate_validate(&session, &changed)); \
    } while (0)

    REJECT_CHANGED(caller, gate.caller + 1U);
    REJECT_CHANGED(app_object, gate.app_object + 4U);
    REJECT_CHANGED(thread_id, gate.thread_id + 1U);
    REJECT_CHANGED(slot_address, gate.slot_address + 4U);
    REJECT_CHANGED(slot_value, gate.slot_value + 1U);
    REJECT_CHANGED(app_vptr, gate.app_vptr + 4U);
    REJECT_CHANGED(idle_argument, 1);
    REJECT_CHANGED(exact_target_verified, 0U);
    REJECT_CHANGED(conflict_free, 0U);
    REJECT_CHANGED(process_generation_stable, 0U);
#undef REJECT_CHANGED

    san9_ping_session_clear(&session);
}

static void test_protocol_round_trips(TestContext *test)
{
    San9PingSession session;
    San9PingSession foreign;
    San9PingMailbox mailbox;
    San9IdleGateSnapshot gate;
    San9IdleGateSnapshot wrong_gate;
    uint8_t request_id[SAN9_PING_REQUEST_ID_SIZE];
    uint8_t challenge[SAN9_PING_DIGEST_SIZE];
    uint8_t request[SAN9_PING_FRAME_SIZE];
    uint8_t response[SAN9_PING_FRAME_SIZE];
    uint8_t foreign_target[SAN9_PING_DIGEST_SIZE];
    uint8_t zero_key[SAN9_PING_KEY_SIZE] = {0};
    uint8_t key_copy[SAN9_PING_KEY_SIZE];
    uint8_t nonce_copy[SAN9_PING_NONCE_SIZE];
    uint8_t fresh_nonce[SAN9_PING_NONCE_SIZE];
    uint8_t target_copy[SAN9_PING_DIGEST_SIZE];
    uint8_t context_copy[SAN9_PING_DIGEST_SIZE];
    int result;

    check(test, sizeof(San9PingMailbox) == SAN9_PING_MAILBOX_SIZE);
    check(test, sizeof(San9PingSession) == SAN9_PING_SESSION_SIZE);
    check(test, initialize_fixture(&session, &mailbox, &gate, request_id, challenge) == SAN9_PING_OK);
    check(test, san9_ping_session_initialize(
        &foreign, zero_key, session.session_nonce, session.target_digest,
        session.context_digest, gate.thread_id, gate.slot_value) == SAN9_PING_INVALID_FRAME);

    result = san9_ping_make_request(&session, 1U, 1000U, 2000U, request_id, challenge, request);
    check(test, result == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_MAILBOX_BUSY);
    check(test, san9_ping_process_one(&session, &mailbox, 1500U, &gate) == SAN9_PING_OK);
    check(test, session.last_accepted_sequence == 1U);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);
    check(test, mailbox.request_state == SAN9_MAILBOX_EMPTY && mailbox.response_state == SAN9_MAILBOX_EMPTY);

    /* Authenticated replay and gap requests receive authenticated rejections. */
    check(test, san9_ping_make_request(&session, 1U, 2000U, 3000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 2500U, &gate) == SAN9_PING_SEQUENCE_REPLAY);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_SEQUENCE_REPLAY);

    check(test, san9_ping_make_request(&session, 3U, 3000U, 4000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 3500U, &gate) == SAN9_PING_SEQUENCE_GAP);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_SEQUENCE_GAP);

    /* Time gates are checked only after a valid HMAC. */
    check(test, san9_ping_make_request(&session, 2U, 4000U, 5000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 5000U, &gate) == SAN9_PING_EXPIRED);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_EXPIRED);

    check(test, san9_ping_make_request(&session, 2U, 6000U, 7000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 5999U, &gate) == SAN9_PING_FROM_FUTURE);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_FROM_FUTURE);
    check(test, san9_ping_make_request(
        &session, 2U, 8000U, 8000U + SAN9_PING_MAX_LIFETIME_MS + 1U,
        request_id, challenge, request) == SAN9_PING_INVALID_FRAME);

    /* Invalid MAC is never answered. Its explicit terminal can be ACK-reset. */
    san9_ping_mailbox_initialize(&mailbox);
    check(test, san9_ping_make_request(&session, 2U, 8000U, 9000U, request_id, challenge, request) == SAN9_PING_OK);
    request[144] ^= 0x80U;
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 8500U, &gate) == SAN9_PING_AUTH_FAILED);
    check(test, mailbox.request_state == SAN9_MAILBOX_REJECTED_UNAUTHENTICATED
        && mailbox.response_state == SAN9_MAILBOX_EMPTY);
    check(test, san9_ping_acknowledge_rejected_request(&mailbox) == SAN9_PING_OK);
    check(test, mailbox.request_state == SAN9_MAILBOX_EMPTY
        && mailbox.response_state == SAN9_MAILBOX_EMPTY);

    /* Structurally bad frames use the same recoverable unauthenticated terminal. */
    check(test, san9_ping_make_request(&session, 2U, 8500U, 9500U, request_id, challenge, request) == SAN9_PING_OK);
    request[0] ^= 1U;
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 8600U, &gate) == SAN9_PING_INVALID_FRAME);
    check(test, mailbox.request_state == SAN9_MAILBOX_REJECTED_UNAUTHENTICATED
        && mailbox.response_state == SAN9_MAILBOX_EMPTY);
    check(test, san9_ping_acknowledge_rejected_request(&mailbox) == SAN9_PING_OK);

    /* The same next sequence succeeds after explicit recovery; no wedge remains. */
    check(test, san9_ping_make_request(&session, 2U, 8500U, 9500U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 8700U, &gate) == SAN9_PING_OK);
    check(test, session.last_accepted_sequence == 2U);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);

    /* Same key but a different target proves binding rejection after authentication. */
    memcpy(key_copy, session.key, sizeof(key_copy));
    memcpy(nonce_copy, session.session_nonce, sizeof(nonce_copy));
    memcpy(target_copy, session.target_digest, sizeof(target_copy));
    memcpy(context_copy, session.context_digest, sizeof(context_copy));
    memcpy(foreign_target, session.target_digest, sizeof(foreign_target));
    foreign_target[0] ^= 1U;
    check(test, san9_ping_session_initialize(
        &foreign, key_copy, nonce_copy, foreign_target, context_copy,
        gate.thread_id, gate.slot_value) == SAN9_PING_OK);
    check(test, san9_ping_make_request(&foreign, 3U, 9000U, 10000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 9500U, &gate) == SAN9_PING_BINDING_MISMATCH);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_BINDING_MISMATCH);

    /* A failed exact gate neither claims nor advances an otherwise valid ping. */
    san9_ping_mailbox_initialize(&mailbox);
    check(test, san9_ping_make_request(&session, 3U, 10000U, 11000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    wrong_gate = gate;
    ++wrong_gate.thread_id;
    check(test, san9_ping_process_one(&session, &mailbox, 10500U, &wrong_gate) == SAN9_PING_GATE_REJECTED);
    check(test, mailbox.request_state == SAN9_MAILBOX_READY && session.last_accepted_sequence == 2U);
    check(test, san9_ping_process_one(&session, &mailbox, 10500U, &gate) == SAN9_PING_OK);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);

    /* A signed response cannot be altered. */
    response[208] ^= 1U;
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_RESPONSE_INVALID);

    /* A monotonic clock rollback faults the whole session before freshness. */
    check(test, san9_ping_make_request(&session, 4U, 10000U, 12000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, session.last_observed_time_ms == 10500U);
    check(test, san9_ping_process_one(&session, &mailbox, 10499U, &gate) == SAN9_PING_CLOCK_ROLLBACK);
    check(test, session.clock_faulted == 1U
        && session.last_observed_time_ms == 10500U
        && session.last_accepted_sequence == 3U);
    check(test, mailbox.request_state == SAN9_MAILBOX_SESSION_FAULT
        && mailbox.response_state == SAN9_MAILBOX_EMPTY);
    check(test, san9_ping_process_one(&session, &mailbox, 11000U, &gate) == SAN9_PING_CLOCK_ROLLBACK);
    check(test, san9_ping_acknowledge_rejected_request(&mailbox) == SAN9_PING_MAILBOX_BUSY);

    /* Recovery requires one atomic reset with a different session nonce. */
    check(test, san9_ping_reset_clock_fault(
        &session, &mailbox, key_copy, nonce_copy, target_copy, context_copy,
        gate.thread_id, gate.slot_value) == SAN9_PING_INVALID_FRAME);
    memcpy(fresh_nonce, nonce_copy, sizeof(fresh_nonce));
    fresh_nonce[0] ^= 0xa5U;
    check(test, san9_ping_reset_clock_fault(
        &session, &mailbox, key_copy, fresh_nonce, target_copy, context_copy,
        gate.thread_id, gate.slot_value) == SAN9_PING_OK);
    check(test, session.initialized == 1U && session.clock_faulted == 0U
        && session.last_observed_time_ms == 0U
        && session.last_accepted_sequence == 0U
        && san9_constant_time_equal(
            session.session_nonce, fresh_nonce, SAN9_PING_NONCE_SIZE));
    check(test, mailbox.request_state == SAN9_MAILBOX_EMPTY
        && mailbox.response_state == SAN9_MAILBOX_EMPTY);

    /* A new session restarts sequence at one; equal monotonic timestamps work. */
    check(test, san9_ping_make_request(&session, 1U, 20000U, 21000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 20500U, &gate) == SAN9_PING_OK);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);
    check(test, san9_ping_make_request(&session, 2U, 20000U, 21000U, request_id, challenge, request) == SAN9_PING_OK);
    check(test, san9_ping_publish_request(&mailbox, request) == SAN9_PING_OK);
    check(test, san9_ping_process_one(&session, &mailbox, 20500U, &gate) == SAN9_PING_OK);
    check(test, san9_ping_take_response(&mailbox, response) == SAN9_PING_OK);
    check(test, san9_ping_verify_response(&session, request, response) == SAN9_PING_OK);
    check(test, san9_ping_reset_clock_fault(
        &session, &mailbox, key_copy, nonce_copy, target_copy, context_copy,
        gate.thread_id, gate.slot_value) == SAN9_PING_INVALID_FRAME);

    san9_ping_session_clear(&foreign);
    san9_ping_session_clear(&session);
    san9_secure_zero(key_copy, sizeof(key_copy));
}

int san9_protocol_run_self_tests(San9BridgeSelfTestReport *report)
{
    San9BridgeSelfTestReport local;
    TestContext test;

    if (report == NULL) {
        report = &local;
    }
    memset(report, 0, sizeof(*report));
    report->structure_size = (uint32_t)sizeof(*report);
    report->ping_only = 1;
    report->execution_authorized = 0;
    report->live_bootstrap_enabled = 0;
    test.report = report;

    test_hashes(&test);
    test_gate_matrix(&test);
    test_protocol_round_trips(&test);
    return report->failed == 0 ? SAN9_PING_OK : SAN9_PING_RESPONSE_INVALID;
}
