/* x86-only ABI bridge. The live profile has one unconditional original-idle
   call on every normal entry. No helper return value can bypass that call. */

    .text
    .p2align 4
    .globl @San9BridgeV52_IdleBridge@12
@San9BridgeV52_IdleBridge@12:
#if SAN9_V52_LIVE_ENABLED == 1
    pushl %ebp
    movl %esp, %ebp
    pushl %ebx
    pushl %esi
    pushl %edi
    subl $20, %esp

    movl %ecx, -16(%ebp)       /* app */
    movl 8(%ebp), %eax
    movl %eax, -20(%ebp)       /* flag */
    cld
    call _san9_v52_enter_idle_depth
    movl %eax, -24(%ebp)       /* outer token/depth; may be zero */

    movl -16(%ebp), %ecx
    pushl -20(%ebp)
    movl $0x00434100, %eax
    call *%eax                 /* exactly one original call; __thiscall ret 4 */
    movl %eax, -28(%ebp)       /* preserve original EAX */
    pushfl
    popl %eax
    andl $0x00000400, %eax
    movl %eax, -32(%ebp)       /* preserve original DF */

    cld
    pushl 4(%ebp)              /* caller return address */
    pushl -24(%ebp)            /* depth/token */
    pushl -20(%ebp)            /* flag */
    pushl -16(%ebp)            /* app */
    call _san9_v52_after_original
    addl $16, %esp

    cmpl $0, -32(%ebp)
    je 1f
    std
    jmp 2f
1:
    cld
2:
    movl -28(%ebp), %eax
    addl $20, %esp
    popl %edi
    popl %esi
    popl %ebx
    popl %ebp
    ret $4
#else
    xorl %eax, %eax
    ret $4
#endif
