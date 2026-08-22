/* x86-only ABI shims for the isolated S5 APPLY_ONCE build.  These wrappers
   construct a native Commerce command for return to the game's existing
   handler driver.  They never call validator, attach, driver, or Apply. */

    .text

    .globl _san9_p1_s5_native_list_ctor
_san9_p1_s5_native_list_ctor:
    movl 4(%esp), %ecx
    movl $0x00470DF0, %eax
    call *%eax
    ret

    .globl _san9_p1_s5_native_list_append
_san9_p1_s5_native_list_append:
    movl 4(%esp), %ecx
    pushl 8(%esp)
    movl $0x0046EF70, %eax
    call *%eax
    ret

    .globl _san9_p1_s5_native_list_dtor
_san9_p1_s5_native_list_dtor:
    movl 4(%esp), %ecx
    movl $0x00470E10, %eax
    call *%eax
    ret

    .globl _san9_p1_s5_native_alloc
_san9_p1_s5_native_alloc:
    pushl 4(%esp)
    movl $0x005DEC20, %eax
    call *%eax
    addl $4, %esp
    ret

    .globl _san9_p1_s5_native_free
_san9_p1_s5_native_free:
    pushl 4(%esp)
    movl $0x005DEC70, %eax
    call *%eax
    addl $4, %esp
    ret

    .globl _san9_p1_s5_native_commerce_ctor
_san9_p1_s5_native_commerce_ctor:
    movl 4(%esp), %ecx
    pushl 12(%esp)
    pushl 12(%esp)
    movl $0x0048B340, %eax
    call *%eax
    ret

    .globl _san9_p1_s6_native_cultivate_ctor
_san9_p1_s6_native_cultivate_ctor:
    movl 4(%esp), %ecx
    pushl 12(%esp)
    pushl 12(%esp)
    movl $0x00488B00, %eax
    call *%eax
    ret

    .globl _san9_p1_s6_native_patrol_ctor
_san9_p1_s6_native_patrol_ctor:
    movl 4(%esp), %ecx
    pushl 12(%esp)
    pushl 12(%esp)
    movl $0x00488410, %eax
    call *%eax
    ret

    .globl _san9_p1_s6_native_train_ctor
_san9_p1_s6_native_train_ctor:
    movl 4(%esp), %ecx
    pushl 12(%esp)
    pushl 12(%esp)
    movl $0x00489D80, %eax
    call *%eax
    ret

    .globl _san9_p1_s6_native_repair_ctor
_san9_p1_s6_native_repair_ctor:
    movl 4(%esp), %ecx
    pushl 12(%esp)
    pushl 12(%esp)
    movl $0x00489080, %eax
    call *%eax
    ret

    .globl _san9_p1_s5_call_saved_command_apply
_san9_p1_s5_call_saved_command_apply:
    movl 4(%esp), %ecx
    movl 8(%esp), %eax
    call *%eax
    ret
