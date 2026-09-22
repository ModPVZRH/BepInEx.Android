using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Hook;

internal static class Program
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Callback(int value);
    private sealed class Receiver { public int Invoke(int value) => value + 7; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (NativeDetour Detour, WeakReference Receiver) Create()
    {
        var receiver = new Receiver();
        return (new NativeDetour((nint)123, new Callback(receiver.Invoke), true), new WeakReference(receiver));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    public static void Main()
    {
        Throws<ArgumentException>(() => new NativeDetour(0, new Callback(x => x)));
        Throws<ArgumentNullException>(() => new NativeDetour((nint)123, null));
        var (detour, receiver) = Create();
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(receiver.IsAlive, "Callback receiver was collected while native hook is live");
        Check(NextInterop.Special, "Special return-buffer flag was lost");
        Check(Marshal.GetDelegateForFunctionPointer<Callback>(detour.Detour)(5) == 12, "Callback invalid after GC");
        detour.Apply();
        Check(NextInterop.HookCalls == 1, "Apply must not hook twice");
        NextInterop.UnhookSucceeds = false;
        Throws<InvalidOperationException>(detour.Dispose);
        Check(detour.OriginalTrampoline != 0, "Failed unhook must retain live state");
        NextInterop.UnhookSucceeds = true;
        detour.Dispose();
        Check(detour.OriginalTrampoline == 0, "Disposed trampoline remains usable");
        var calls = NextInterop.UnhookCalls;
        detour.Dispose();
        Check(NextInterop.UnhookCalls == calls, "Dispose must not remove a later hook at the same address");
        Throws<ObjectDisposedException>(detour.Apply);
        NextInterop.HookSucceeds = false;
        Throws<InvalidOperationException>(() => new NativeDetour((nint)456, new Callback(x => x), true));
        GC.KeepAlive(detour);
        Console.WriteLine("PASS: GC rooting, return-buffer flag, hook failure, unhook failure, idempotent disposal, no reuse");
    }
}

namespace BepInEx.Unity.IL2CPP
{
    internal static class NextInterop
    {
        internal static bool HookSucceeds = true, UnhookSucceeds = true, Special;
        internal static int HookCalls, UnhookCalls;
        internal static nint hook(nint target, nint callback, bool special)
        {
            HookCalls++; Special = special;
            return HookSucceeds ? (nint)999 : 0;
        }
        internal static bool unhook_checked(nint target) { UnhookCalls++; return UnhookSucceeds; }
    }
}

namespace Il2CppInterop.Runtime.Injection
{
    public interface IDetour : IDisposable
    {
        nint Target { get; }
        nint Detour { get; }
        nint OriginalTrampoline { get; }
        void Apply();
        T GenerateTrampoline<T>() where T : Delegate;
    }
}
