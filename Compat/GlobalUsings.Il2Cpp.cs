// IL2CPP backend (net6.0) global usings.
//
// Strategy: import the Il2Cpp* game namespaces globally so the rest of the source uses
// UNQUALIFIED game type names (ItemDefinition, SeedDefinition, ItemSelector, ...) that resolve
// identically under the Mono backend (which imports the plain ScheduleOne.* namespaces in
// Compat/GlobalUsings.Mono.cs). UnityEngine is NOT prefixed in il2cpp interop, so it is
// backend-agnostic.
//
// NOTE 1: because UnityEngine is imported here and System is imported implicitly, the bare
//         identifier `Object` is ambiguous - always write `UnityEngine.Object`.
// NOTE 2: the ScheduleOne ROOT namespace is deliberately NOT imported globally - it contains
//         a `Console` type that would collide with System.Console. `Registry` is aliased
//         instead, which is the only root-namespace type this mod needs.

global using UnityEngine;

global using Il2CppScheduleOne.ItemFramework;           // ItemDefinition, StorableItemDefinition, AdditiveDefinition
global using Il2CppScheduleOne.Growing;                 // SeedDefinition, Plant, PlantGrowthStage, PlantHarvestable
global using Il2CppScheduleOne.Product;                 // ProductDefinition, ProductManager
global using Il2CppScheduleOne.Management;              // ItemField, ManagementUtilities, ManagementInterface
global using Il2CppScheduleOne.UI.Management;           // ItemFieldUI, ItemSelector, ClipboardScreen
global using Il2CppScheduleOne.DevUtilities;            // Singleton<T>, NetworkSingleton<T>, PlayerSingleton<T>
global using Il2CppScheduleOne.Effects;                 // Effect
global using Il2CppScheduleOne.Core.Items.Framework;    // BaseItemDefinition, EItemCategory

global using GameRegistry = Il2CppScheduleOne.Registry;
global using GameInput = Il2CppScheduleOne.GameInput;
