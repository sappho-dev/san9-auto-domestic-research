#include "san9_bridge_v52.h"
#include "san9_v52_lifecycle.h"

#include <limits.h>

int san9_v52_lifecycle_cancel_allowed(int32_t state, int allow_writing)
{
    return state == SAN9_V52_BOOTSTRAP_SEALED
        || state == SAN9_V52_BOOTSTRAP_CLAIMED
        || state == SAN9_V52_BOOTSTRAP_INSTALLING
        || (allow_writing != 0 && state == SAN9_V52_BOOTSTRAP_WRITING);
}

int san9_v52_lifecycle_reject_allowed(int32_t current_state, int32_t owned_state)
{
    int owned_state_is_rejectable = owned_state == SAN9_V52_BOOTSTRAP_CLAIMED
        || owned_state == SAN9_V52_BOOTSTRAP_INSTALLING
        || owned_state == SAN9_V52_BOOTSTRAP_COMMITTING;
    return owned_state_is_rejectable && current_state == owned_state;
}

int san9_v52_lifecycle_commit_allowed(int32_t current_state, int validation_result)
{
    return current_state == SAN9_V52_BOOTSTRAP_INSTALLING
        && validation_result == SAN9_V52_OK;
}

int san9_v52_lifecycle_deadline_decision(
    int32_t state,
    uint64_t now_ms,
    uint64_t expires_at_ms)
{
    if (state == SAN9_V52_BOOTSTRAP_READY
        || state == SAN9_V52_BOOTSTRAP_REJECTED
        || state == SAN9_V52_BOOTSTRAP_STOPPED) {
        return SAN9_V52_DEADLINE_TERMINAL;
    }
    if (state == SAN9_V52_BOOTSTRAP_COMMITTING
        || state == SAN9_V52_BOOTSTRAP_REJECTING) {
        return SAN9_V52_DEADLINE_WAIT_COMMIT;
    }
    if (expires_at_ms == 0U || now_ms < expires_at_ms) {
        return SAN9_V52_DEADLINE_WAIT;
    }
    if (san9_v52_lifecycle_cancel_allowed(state, 0)) {
        return SAN9_V52_DEADLINE_TRY_CANCEL;
    }
    return SAN9_V52_DEADLINE_RESTART_REQUIRED;
}

int san9_v52_lifecycle_ping_completion(
    int32_t request_state,
    int32_t response_state,
    int32_t ping_count)
{
    if (ping_count < 0 || ping_count > 1) {
        return SAN9_V52_PING_FAIL_MULTIPLE;
    }
    if (response_state == SAN9_MAILBOX_READY) {
        if (request_state == SAN9_MAILBOX_COMPLETE && ping_count == 1) {
            return SAN9_V52_PING_TAKE_ONCE;
        }
        if (ping_count == 0
            && (request_state == SAN9_MAILBOX_READY
                || request_state == SAN9_MAILBOX_CLAIMED
                || request_state == SAN9_MAILBOX_COMPLETE)) {
            return SAN9_V52_PING_WAIT;
        }
        return SAN9_V52_PING_FAIL_INVALID;
    }
    if (response_state == SAN9_MAILBOX_EMPTY && ping_count <= 1
        && (request_state == SAN9_MAILBOX_READY
            || request_state == SAN9_MAILBOX_CLAIMED
            || request_state == SAN9_MAILBOX_COMPLETE)) {
        return SAN9_V52_PING_WAIT;
    }
    return SAN9_V52_PING_FAIL_INVALID;
}

uint32_t san9_v52_lifecycle_next_failure_streak(
    uint32_t current_streak,
    int operation_succeeded)
{
    if (operation_succeeded) {
        return 0U;
    }
    return current_streak == UINT_MAX ? UINT_MAX : current_streak + 1U;
}

int san9_v52_lifecycle_recovery_classify(const San9V52RecoveryFacts *facts)
{
    if (facts == NULL) {
        return SAN9_V52_RECOVERY_RESTART_REQUIRED;
    }
    if (facts->indeterminate_commit != 0U
        || facts->unhook_failure_streak >= 2U
        || (facts->pin_succeeded != 0U && facts->slot_failed != 0U)
        || (facts->ready_seen != 0U
            && (facts->post_ready_failed != 0U
                || facts->ping_failed != 0U))) {
        return SAN9_V52_RECOVERY_RESTART_REQUIRED;
    }
    if (facts->atom_failure_streak >= 2U) {
        return SAN9_V52_RECOVERY_CLEANUP_INCOMPLETE;
    }
    return SAN9_V52_RECOVERY_CLEAN;
}
