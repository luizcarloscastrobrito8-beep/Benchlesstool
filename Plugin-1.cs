using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BenchlessTools
{
    [BepInPlugin("com.benchlesstools.silksong", "Benchless Tools", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static ConfigEntry<string> _names;

        private void Awake()
        {
            _names = Config.Bind(
                "General",
                "MethodNames",
                "CanChangeEquips,CanChangeEquip,CanChangeTools,CanEquipTools",
                "Nomes (separados por virgula) dos metodos/propriedades bool que bloqueiam trocar ferramentas fora do banco.");

            var wanted = new HashSet<string>(
                _names.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

            if (asm == null)
            {
                Logger.LogError("Assembly-CSharp nao encontrado.");
                return;
            }

            var harmony = new Harmony("com.benchlesstools.silksong");
            var postfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ForceTrue),
                BindingFlags.Static | BindingFlags.NonPublic));

            int patched = 0;
            foreach (var type in SafeTypes(asm))
            {
                if (type.ContainsGenericParameters) continue;

                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.DeclaredOnly;

                foreach (var m in type.GetMethods(flags))
                {
                    if (m.ReturnType != typeof(bool) || m.GetParameters().Length != 0) continue;

                    var n = m.Name.StartsWith("get_") ? m.Name.Substring(4) : m.Name;
                    if (!wanted.Contains(n)) continue;

                    try
                    {
                        harmony.Patch(m, postfix: postfix);
                        patched++;
                        Logger.LogInfo($"Patch aplicado: {type.FullName}.{m.Name}");
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning($"Falhou em {type.FullName}.{m.Name}: {e.Message}");
                    }
                }
            }

            if (patched == 0)
                Logger.LogWarning("Nenhum metodo encontrado. Abra Assembly-CSharp.dll no dnSpy/ILSpy, ache o bool que checa o banco ao equipar ferramentas e coloque o nome em MethodNames no arquivo .cfg.");
        }

        private static IEnumerable<Type> SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        private static void ForceTrue(ref bool __result)
        {
            __result = true;
        }
    }
}
