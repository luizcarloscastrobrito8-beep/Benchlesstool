using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace BenchlessTools
{
    [BepInPlugin("com.benchlesstools.silksong", "Benchless Tools", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private static ConfigEntry<string> _names;

        private void Awake()
        {
            Logger.LogInfo("Benchless Tools carregado.");
            try { Run(); }
            catch (Exception e) { Logger.LogError("Erro: " + e); }
        }

        private void Run()
        {
            _names = Config.Bind(
                "General",
                "MethodNames",
                "CanChangeEquips,CanChangeEquip,CanChangeTools,CanEquipTools,IsAtBench,AtBench",
                "Nomes (separados por virgula) dos metodos bool sem parametros que bloqueiam trocar ferramentas fora do banco.");

            var wanted = new HashSet<string>(
                _names.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));

            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

            if (asm == null)
            {
                Logger.LogError("Assembly-CSharp nao encontrado.");
                return;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.DeclaredOnly;

            var harmony = new Harmony("com.benchlesstools.silksong");
            var postfix = new HarmonyMethod(typeof(Plugin).GetMethod(nameof(ForceTrue),
                BindingFlags.Static | BindingFlags.NonPublic));

            var dump = new StringBuilder();
            int patched = 0;

            foreach (var type in SafeTypes(asm))
            {
                try
                {
                    foreach (var f in type.GetFields(flags))
                    {
                        if (f.Name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0)
                            dump.AppendLine($"CAMPO  {type.FullName}.{f.Name} : {f.FieldType.Name}");
                    }

                    foreach (var m in type.GetMethods(flags))
                    {
                        bool bench = m.Name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool equipBool = m.ReturnType == typeof(bool) &&
                                         m.Name.IndexOf("equip", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (bench || equipBool)
                        {
                            var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
                            dump.AppendLine($"METODO {type.FullName}.{m.Name}({ps}) : {m.ReturnType.Name}");
                        }

                        if (type.ContainsGenericParameters) continue;
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
                catch (Exception) { }
            }

            try
            {
                var path = Path.Combine(Paths.BepInExRootPath, "BenchlessTools_dump.txt");
                File.WriteAllText(path, dump.ToString());
                Logger.LogInfo("Lista salva em: " + path);
            }
            catch (Exception e)
            {
                Logger.LogWarning("Nao consegui salvar a lista: " + e.Message);
            }

            if (patched == 0)
                Logger.LogWarning("Nenhum metodo encontrado. Envie o arquivo BenchlessTools_dump.txt.");
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
