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
    /// </summary>
    internal static class ProbeScanner
    {
        private const string ProbeTypeName = "ClipwiseProbe";

        /// <summary>
        /// Assemblies never worth searching for a mod's probe, matched on the start of the simple name.
        ///
        /// This is not just a speed filter. The fallback search calls <c>Assembly.GetTypes()</c>, which forces
        /// every type in the assembly to resolve - and doing that to the generated IL2CPP interop assemblies can
        /// take the whole runtime down rather than throw something catchable. A mod's probe never lives in any of
        /// these, so there is nothing to gain by looking.
        /// </summary>
        private static readonly string[] SkipPrefixes =
        {
            "Il2Cpp", "Unity", "UnityEngine", "System", "mscorlib", "netstandard", "MelonLoader",
            "0Harmony", "Newtonsoft", "Microsoft", "Mono.", "Tomlet", "Iced", "AsmResolver",
            "MonoMod", "Cpp2IL", "Disarm", "StableNameDotNet", "SemanticVersioning", "Bootstrap",
        };

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
                    // The cheap lookup by name is safe on anything; the enumerating fallback is not, so it only
                    // runs on assemblies that could plausibly be a mod.
                    Type probe = asm.GetType(ProbeTypeName, false);
                    if (probe == null && !Skip(asm)) probe = FindByLeafName(asm, ProbeTypeName);
                    if (probe == null) continue;

                    MethodInfo reg = probe.GetMethod("Register",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (reg == null) continue;

                    reg.Invoke(null, null);
                    found++;
                    Core.LogDebug($"Clipwise: ran ClipwiseProbe from {asm.GetName().Name}.");
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

        /// <summary>True for a runtime, Unity or interop assembly that must never be type-enumerated.</summary>
        private static bool Skip(Assembly asm)
        {
            string name = Safe(asm);
            if (string.IsNullOrEmpty(name) || name == "?") return true;
            for (int i = 0; i < SkipPrefixes.Length; i++)
                if (name.StartsWith(SkipPrefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Accepts a probe in any namespace, as long as it is a static class with that leaf name.</summary>
        private static Type FindByLeafName(Assembly asm, string leaf)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            catch { return null; }
            if (types == null) return null;

            foreach (Type t in types)
                if (t != null && t.IsClass && t.IsAbstract && t.IsSealed && t.Name == leaf) return t;   // static class
            return null;
        }
    }
}
