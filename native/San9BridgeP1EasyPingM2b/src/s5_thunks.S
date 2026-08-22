/* x86-only ABI shim for the S5 NO_APPLY timing probe.  This file contains
   exactly one game call: DomesticController::DispatchEvent(this, 0x2711).
   No allocator, command constructor, validator, attach driver, or Apply
   address is present in this build. */

    .text
    .globl _san9_p1_s5_call_root_event
    .def _san9_p1_s5_call_root_event; .scl 2; .type 32; .endef
_san9_p1_s5_call_root_event:
    pushl %ebp
    movl %esp, %ebp
    movl 8(%ebp), %ecx
    pushl 12(%ebp)
    movl $0x005179B0, %eax
    call *%eax
    movl %ebp, %esp
    popl %ebp
    ret
