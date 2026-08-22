/* x86 S5 v8 DomesticCommandMenu +0xA4 wrapper.
   Game ABI: ECX=this, no stack arguments, plain ret.  The entry helper owns
   all menu identity/CAS work.  After the native original returns, this
   wrapper passes only an opaque entry token and caller to the start helper;
   it never reads or forwards the menu pointer again. */
    .text
    .p2align 4
    .globl @san9_p1_s5_v8_menu_tick_bridge@8
@san9_p1_s5_v8_menu_tick_bridge@8:
    pushl %ebp
    movl %esp, %ebp
    pushl %ebx
    pushl %esi
    pushl %edi
    subl $576, %esp

    movl %ecx, -16(%ebp)
    movl 4(%ebp), %eax
    movl %eax, -20(%ebp)

    pushfl
    popl %eax
    andl $0x00000400, %eax
    movl %eax, -24(%ebp)
    cld
    leal 15(%esp), %eax
    andl $-16, %eax
    movl %eax, -28(%ebp)
    fxsave (%eax)
    movl %esp, -32(%ebp)
    andl $-16, %esp
    pushl -20(%ebp)
    pushl -16(%ebp)
    call _san9_p1_s5_v8_menu_entry
    addl $8, %esp
    movl -32(%ebp), %esp
    movl %eax, -36(%ebp)
    movl -28(%ebp), %eax
    fxrstor (%eax)
    cmpl $0, -24(%ebp)
    je 1f
    std
    jmp 2f
1:
    cld
2:
    movl -16(%ebp), %ecx
#if SAN9_S5_V8_OFFLINE_FAKE
    xorl %edx, %edx
    call @san9_p1_s5_v8_fake_original@8
#else
    movl $0x004175C0, %eax
    call *%eax
#endif
    movl %eax, -40(%ebp)

    pushfl
    popl %eax
    andl $0x00000400, %eax
    movl %eax, -44(%ebp)
    cld
    leal 15(%esp), %eax
    andl $-16, %eax
    movl %eax, -48(%ebp)
    fxsave (%eax)
    movl %esp, -52(%ebp)
    andl $-16, %esp
    pushl -20(%ebp)
    pushl -36(%ebp)
    call _san9_p1_s5_v8_after_original
    addl $8, %esp
    movl -52(%ebp), %esp
    movl -48(%ebp), %eax
    fxrstor (%eax)
    cmpl $0, -44(%ebp)
    je 3f
    std
    jmp 4f
3:
    cld
4:
    movl -40(%ebp), %eax
    addl $576, %esp
    popl %edi
    popl %esi
    popl %ebx
    popl %ebp
    ret
