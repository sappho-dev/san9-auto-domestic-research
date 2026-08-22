#ifndef SAN9_V52_LIFECYCLE_H
#define SAN9_V52_LIFECYCLE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

enum San9V52DeadlineDecision {
    SAN9_V52_DEADLINE_WAIT = 0,
    SAN9_V52_DEADLINE_TRY_CANCEL = 1,
    SAN9_V52_DEADLINE_WAIT_COMMIT = 2,
    SAN9_V52_DEADLINE_TERMINAL = 3,
    SAN9_V52_DEADLINE_RESTART_REQUIRED = 4
};

enum San9V52PingCompletionDecision {
    SAN9_V52_PING_WAIT = 0,
    SAN9_V52_PING_TAKE_ONCE = 1,
    SAN9_V52_PING_FAIL_MULTIPLE = 2,
    SAN9_V52_PING_FAIL_INVALID = 3
};

enum San9V52RecoveryClass {
    SAN9_V52_RECOVERY_CLEAN = 0,
    SAN9_V52_RECOVERY_CLEANUP_INCOMPLETE = 1,
    SAN9_V52_RECOVERY_RESTART_REQUIRED = 2
};

typedef struct San9V52RecoveryFacts {
    uint32_t pin_succeeded;
    uint32_t slot_failed;
    uint32_t ready_seen;
    uint32_t post_ready_failed;
    uint32_t ping_failed;
    uint32_t indeterminate_commit;
    uint32_t unhook_failure_streak;
    uint32_t atom_failure_streak;
} San9V52RecoveryFacts;

/* These are pure state-policy helpers.  Production controller/DLL code and
   the offline synthetic suite call the same functions.  Atomic ownership is
   still acquired by the caller with a compare/exchange after the decision. */
int san9_v52_lifecycle_cancel_allowed(int32_t state, int allow_writing);
int san9_v52_lifecycle_reject_allowed(int32_t current_state, int32_t owned_state);
int san9_v52_lifecycle_commit_allowed(int32_t current_state, int validation_result);

int san9_v52_lifecycle_deadline_decision(
    int32_t state,
    uint64_t now_ms,
    uint64_t expires_at_ms);

int san9_v52_lifecycle_ping_completion(
    int32_t request_state,
    int32_t response_state,
    int32_t ping_count);

uint32_t san9_v52_lifecycle_next_failure_streak(
    uint32_t current_streak,
    int operation_succeeded);

int san9_v52_lifecycle_recovery_classify(const San9V52RecoveryFacts *facts);

#ifdef __cplusplus
}
#endif

#endif
