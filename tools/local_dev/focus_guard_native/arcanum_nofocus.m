// ARC-DEV-403: the smoke windows' focus guard (macOS only). `./arcanum smoke` loads this small library into the Godot
// process of each window smoke (DYLD_INSERT_LIBRARIES; the Godot app's entitlements allow it), so the viewer window never
// becomes the active application, never takes the keyboard focus from what the owner is working in, and shows no Dock icon
// and no window on the screen. Godot makes itself the foreground application at start (TransformProcessType to foreground,
// setActivationPolicy: Regular, activateIgnoringOtherApps:, makeKeyAndOrderFront:); this turns exactly those calls off. The
// process becomes a background-only application while its window keeps drawing, so the viewer's own frame readbacks (viewport
// textures) work as before. The smokes drive the viewer with synthetic input and simulate the window focus themselves.
// Not loaded by any player, package or hand-started run.
#import <AppKit/AppKit.h>
#import <ApplicationServices/ApplicationServices.h>
#import <objc/runtime.h>

static id keepAwake;  // the App Nap activity: a process nobody sees must keep its full speed

// The Carbon call that brings a process to the foreground, interposed (dyld's __interpose section): the process becomes a
// background-only application (no Dock icon, no menu bar, never active, no window shown) whatever state is asked for.
static OSStatus arcanumTransformProcess(const ProcessSerialNumber *psn, ProcessApplicationTransformState state) {
    return TransformProcessType(psn, kProcessTransformToBackgroundApplication);
}
__attribute__((used)) static struct { const void *replacement; const void *replacee; } arcanumInterpose[]
    __attribute__((section("__DATA,__interpose"))) = {{(const void *)arcanumTransformProcess, (const void *)TransformProcessType}};

static void replace(Class cls, SEL selector, id block) {
    Method method = class_getInstanceMethod(cls, selector);
    if (method) method_setImplementation(method, imp_implementationWithBlock(block));
}

__attribute__((constructor)) static void arcanumNoFocus(void) {
    @autoreleasepool {
        // The variable is inherited by anything Godot starts (dotnet, shells): only the Godot process itself is guarded.
        if (![[[NSProcessInfo processInfo] processName] hasPrefix:@"Godot"]) return;
        replace([NSApplication class], @selector(activateIgnoringOtherApps:), ^(id self, BOOL flag) {});
        if ([NSApplication instancesRespondToSelector:@selector(activate)])
            replace([NSApplication class], @selector(activate), ^(id self) {});
        Method policy = class_getInstanceMethod([NSApplication class], @selector(setActivationPolicy:));
        if (policy) {
            BOOL (*original)(id, SEL, NSApplicationActivationPolicy) = (void *)method_getImplementation(policy);
            replace([NSApplication class], @selector(setActivationPolicy:), ^BOOL(id self, NSApplicationActivationPolicy value) {
                return original(self, @selector(setActivationPolicy:), NSApplicationActivationPolicyProhibited);
            });
        }
        replace([NSWindow class], @selector(makeKeyAndOrderFront:), ^(NSWindow *self, id sender) { [self orderFront:sender]; });
        replace([NSWindow class], @selector(makeKeyWindow), ^(NSWindow *self) {});
        keepAwake = [[NSProcessInfo processInfo]
            beginActivityWithOptions:NSActivityUserInitiatedAllowingIdleSystemSleep reason:@"Arcanum smoke window"];
    }
}
