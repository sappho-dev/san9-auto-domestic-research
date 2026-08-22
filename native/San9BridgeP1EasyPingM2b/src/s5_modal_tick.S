/* x86 DomesticCommandMenu vtable+0xA4 probe wrapper.
   ABI: ECX=this, no stack arguments, plain ret.  The native original is the
   first call and is made exactly once.  The post helper runs with DF clear,
   aligned stack, protected x87/MXCSR state, and preserved nonvolatiles. */
    .text
    .p2align 4
    .globl @san9_p1_s5_modal_tick_bridge@8
@san9_p1_s5_modal_tick_bridge@8:
    pushl %ebp
    movl %esp, %ebp
    pushl %ebx
    pushl %esi
    pushl %edi
    subl $560, %esp

    movl %ecx, -16(%ebp)
    movl 4(%ebp), %eax
    movl %eax, -20(%ebp)
    movl -16(%ebp), %ecx
#if SAN9_S5_MODAL_OFFLINE_FAKE
    xorl %edx, %edx
    call @san9_p1_s5_modal_fake_original@8
#else
    movl $0x004175C0, %eax
    call *%eax
#endif
    movl %eax, -24(%ebp)
    pushfl
    popl %eax
    andl $0x00000400, %eax
    movl %eax, -28(%ebp)

    cld
    leal 15(%esp), %eax
    andl $-16, %eax
    movl %eax, -32(%ebp)
    fxsave (%eax)
    movl %esp, -36(%ebp)
    andl $-16, %esp
    pushl -20(%ebp)
    pushl -16(%ebp)
    call _san9_p1_s5_modal_after_original
    addl $8, %esp
    movl -36(%ebp), %esp
    movl -32(%ebp), %eax
    fxrstor (%eax)

    cmpl $0, -28(%ebp)
    je 1f
    std
    jmp 2f
1:
    cld
2:
    movl -24(%ebp), %eax
    addl $560, %esp
    popl %edi
    popl %esi
    popl %ebx
    popl %ebp
    ret
