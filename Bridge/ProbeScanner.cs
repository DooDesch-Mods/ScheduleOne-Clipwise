using System;
using System.Reflection;

namespace Clipwise.Bridge
{
    /// <summary>
    /// Finds and runs every loaded mod's <c>ClipwiseProbe.Register()</c>.
    ///
    /// This is the half of the handshake the shim cannot do alone. The shim queues calls made before Clipwise is up
    /// and replays them the next time the consumer calls in - but a mod that registers once and never calls again
    /// would keep its registrations queued forever if it happened to load first. Scanning from this side closes
    /// that direction: whoever loaded earlier gets picked up here, whoever loads later binds immediately through
    /// the shim. Registration is an idempotent upsert, so being reached by both routes is harmless.
    ///
    /// The lookup is a plain <c>Assembly.GetType(name)</c> and nothing else. Enumerating an assembly's types
    /// (<c>GetTypes()</c>) forces every type in it to resolve, and under IL2CPP that takes the whole process down
    /// uncatchably - a <c>try/catch</c> around it proves nothing, and a skip-list only makes it rarer, because the
    /// assembly that kills it is whichever mod happens to be loaded. So the probe has to be findable by name:
    /// <c>ClipwiseProbe</c> in the global namespace, which is what the convention asks for anyway.
    /// </summary>
    internal static class ProbeScanner
    {
        private const string ProbeTypeName = "ClipwiseProbe";

        public static void RunAll()
        {
            Assembly self = typeof(ProbeScanner).Assembly;
            int found = 0;

            Assembly[] assemblies;
            try { assemblies = AppDomain.CurrentDomain.GetAssemblies(); }
            catch (Exception e) { Core.Log.Warning("Clipwise: could not enumerate assemblies: " + e.Message); return; }

            foreach (Assembly asm in assemblies)
            {
                if (asm == self) continue;
                try
                {
                    Type probe = asm.GetType(ProbeTypeName, false);
                    if (probe == null) continue;

                    MethodInfo reg = probe.GetMethod("Register",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (reg == null) continue;

                    reg.Invoke(null, null);
                    found++;
                    Core.LogDebug($"Clipwise: ran ClipwiseProbe from {Safe(asm)}.");
                }
                catch (Exception e)
                {
                    // One mod's broken probe must not stop the others, and must not take Clipwise down with it.
                    Core.Log.Warning($"Clipwise: the ClipwiseProbe in '{Safe(asm)}' threw: {e.InnerException?.Message ?? e.Message}");
                }
            }

            if (found > 0) Core.Log.Msg($"Clipwise: picked up {found} mod probe(s).");
        }

        private static string Safe(Assembly asm)
        {
            try { return asm.GetName().Name; } catch { return "?"; }
        }
    }
}
