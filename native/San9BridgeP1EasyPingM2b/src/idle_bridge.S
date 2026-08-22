/* x86-only safe-point bridge.  Every normal entry calls the original idle
   routine exactly once.  The post helper and depth leave run with DF clear,
   a 16-byte-aligned call stack, and the original x87/MXCSR image protected by
   FXSAVE/FXRSTOR.  EAX, DF, EBX, ESI, EDI, EBP and ESP are restored exactly. */
    .text
    .p2align 4
    .globl @San9BridgeP1M2b_IdleBridge@12
@San9BridgeP1M2b_IdleBridge@12:
    pushl %ebp
    movl %esp, %ebp
    pushl %ebx
    pushl %esi
    pushl %edi
    subl $560, %esp

    movl %ecx, -16(%ebp)
    movl 8(%ebp), %eax
    movl %eax, -20(%ebp)
    cld
    call _san9_p1_m2b_enter_idle_depth
    movl %eax, -24(%ebp)

    movl -16(%ebp), %ecx
    pushl -20(%ebp)
    movl $0x00434100, %eax
    call *%eax
    movl %eax, -28(%ebp)
    pushfl
    popl %eax
    andl $0x00000400, %eax
    movl %eax, -32(%ebp)

    cld
    leal 15(%esp), %eax
    andl $-16, %eax
    movl %eax, -36(%ebp)
    fxsave (%eax)
    movl %esp, -40(%ebp)
    andl $-16, %esp
    pushl 4(%ebp)
    pushl -24(%ebp)
    pushl -20(%ebp)
    pushl -16(%ebp)
    call _san9_p1_m2b_after_original
    addl $16, %esp

    pushl -24(%ebp)
    call _san9_p1_m2b_leave_idle_depth
    addl $4, %esp
    movl -40(%ebp), %esp
    movl -36(%ebp), %eax
    fxrstor (%eax)

    cmpl $0, -32(%ebp)
    je 1f
    std
    jmp 2f
1:
    cld
2:
    movl -28(%ebp), %eax
    addl $560, %esp
    popl %edi
    popl %esi
    popl %ebx
    popl %ebp
    ret $4
