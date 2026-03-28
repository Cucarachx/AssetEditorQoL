using ColossalFramework.IO;
using ColossalFramework.Threading;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using CitiesHarmony.API;





namespace SubfolderImport
{
    public class SubfolderImportMod : IUserMod
    {
        public string Name => "Asset Editor Quality of Life";
        public string Description => "Extends the Asset Editor with subfolder support and mesh reload functionality";

        

        public void OnEnabled()
        {
            HarmonyHelper.DoOnHarmonyReady(() =>
            {
                var harmony = new Harmony("com.cucarachx.asseteditorqol");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            });
            HarmonyHelper.DoOnHarmonyReady(() =>
            {
                var harmony = new Harmony("com.jodurantem.asseteditorqol");
                harmony.PatchAll(Assembly.GetExecutingAssembly()); // patches de SubfolderImport
                harmony.PatchAll(typeof(ReloadMesh.ReloadState).Assembly); // patches de ReloadMesh
            });
        }

        public void OnDisabled()
        {
            if (HarmonyHelper.IsHarmonyInstalled)
            {
                var harmony = new Harmony("com.cucarachx.asseteditorqol");
                harmony.UnpatchAll("com.cucarachx.asseteditorqol");
            }
        }
    }
}

    [HarmonyPatch(typeof(AssetImporterAssetImport), "RefreshAssetList",
        new Type[] { typeof(string[]) })]
    public class RefreshAssetListPatch
    {
        public static void Postfix(AssetImporterAssetImport __instance,
                                   string[] extensions)
        {
            try
            {
                string importPath = AssetImporterAssetImport.assetImportPath;
                DirectoryInfo rootDir = new DirectoryInfo(importPath);

                if (!rootDir.Exists) return;

                // Acceder a m_FileList via reflection ya que es privada
                var fileListField = typeof(AssetImporterAssetImport)
                    .GetField("m_FileList",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                UIListBox fileList = (UIListBox)fileListField.GetValue(__instance);

                if (fileList == null) return;

                // Obtener los items que ya agregó el método original
                List<string> list = new List<string>(fileList.items);

                var lodSignatureField = typeof(AssetImporterAssetImport)
                    .GetField("sLODModelSignature",
                        BindingFlags.NonPublic | BindingFlags.Static);
                string lodSignature = (string)lodSignatureField.GetValue(null);

                // Escanear solo subcarpetas (el método original ya manejó la raíz)
                foreach (DirectoryInfo subDir in rootDir.GetDirectories())
                {
                    FileInfo[] files = subDir.GetFiles("*",
                        SearchOption.AllDirectories);

                    foreach (FileInfo file in files)
                    {
                        // Ignorar archivos LOD como hace el método original
                        if (Path.GetFileNameWithoutExtension(file.Name)
                            .EndsWith(lodSignature,
                                StringComparison.OrdinalIgnoreCase)) continue;

                        // Verificar extensión
                        foreach (string ext in extensions)
                        {
                            if (string.Compare(
                                Path.GetExtension(file.Name), ext,
                                StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                // Guardar como ruta relativa
                                string relativePath = file.FullName
                                    .Replace(importPath, "")
                                    .TrimStart(Path.DirectorySeparatorChar);
                                list.Add(relativePath);
                                break;
                            }
                        }
                    }
                }

                fileList.items = list.ToArray();
            }
            catch (Exception ex)
            {
                Debug.LogError("SubfolderImport error: " + ex);
            }
        }
    }


[HarmonyPatch(typeof(AssetImporterTextureLoader), "LoadTextures",
    new Type[] { typeof(Task<GameObject>), typeof(GameObject),
                 typeof(AssetImporterTextureLoader.ResultType[]),
                 typeof(string), typeof(string), typeof(bool),
                 typeof(int), typeof(bool), typeof(bool) })]
public class LoadTexturesPatch
{
    public static void Prefix(ref string path, ref string modelName)
    {
        try
        {
            Debug.Log("SubfolderImport BEFORE - path: " + path + " modelName: " + modelName);
            // Construir la ruta completa del modelo
            string fullPath = Path.Combine(path, modelName);

            // Separar correctamente la subcarpeta del nombre del archivo
            path = Path.GetDirectoryName(fullPath);
            modelName = Path.GetFileName(fullPath);

            Debug.Log("SubfolderImport AFTER - path: " + path + " modelName: " + modelName);        
        }
        catch (Exception ex)
        {
            Debug.LogError("SubfolderImport LoadTextures patch error: " + ex);
        }
    }
}


[HarmonyPatch(typeof(ImportAssetLodded), "CreateLODObject")]
public class CreateLODObjectPatch
{
    // Diccionario por instancia en lugar de campos estáticos
    private class InstanceValues
    {
        public string Path;
        public string Filename;
    }

    private static readonly Dictionary<int, InstanceValues>
        OriginalValues = new Dictionary<int, InstanceValues>();

    public static void Prefix(ImportAssetLodded __instance)
    {
        try
        {
            var pathField = typeof(ImportAsset)
                .GetField("m_Path",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            var filenameField = typeof(ImportAsset)
                .GetField("m_Filename",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            string path = (string)pathField.GetValue(__instance);
            string filename = (string)filenameField.GetValue(__instance);

            // Guardar por instancia usando el hash de la instancia
            OriginalValues[__instance.GetHashCode()] = new InstanceValues
            {
                Path = path,
                Filename = filename
            };

            string fullPath = Path.Combine(path, filename);
            string newPath = Path.GetDirectoryName(fullPath);
            string newFilename = Path.GetFileName(fullPath);

            if (newPath != path)
            {
                pathField.SetValue(__instance, newPath);
                filenameField.SetValue(__instance, newFilename);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("SubfolderImport CreateLODObject Prefix error: " + ex);
        }
    }

    public static void Postfix(ImportAssetLodded __instance)
    {
        try
        {
            int key = __instance.GetHashCode();
            if (!OriginalValues.ContainsKey(key)) return;

            var pathField = typeof(ImportAsset)
                .GetField("m_Path",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            var filenameField = typeof(ImportAsset)
                .GetField("m_Filename",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            var values = OriginalValues[key];
            pathField.SetValue(__instance, values.Path);
            filenameField.SetValue(__instance, values.Filename);

            // Limpiar el diccionario
            OriginalValues.Remove(key);
        }
        catch (Exception ex)
        {
            Debug.LogError("SubfolderImport CreateLODObject Postfix error: " + ex);
        }
    }
}

[HarmonyPatch(typeof(AssetImporterTextureLoader), "FindTexture",
    new Type[] { typeof(string), typeof(AssetImporterTextureLoader.SourceType) })]
public class FindTexturePatch
{
    private static readonly Regex VersionSuffix = new Regex(
    @"_[vV]\d+[a-zA-Z]?(?=_lod$)|_[vV]\d+[a-zA-Z]?$",
    RegexOptions.Compiled);

    private static string[] _extensions;
    private static string[] _signatures;

    public static void Prepare()
    {
        var extensionsField = typeof(AssetImporterTextureLoader)
            .GetField("SourceTextureExtensions",
                BindingFlags.NonPublic | BindingFlags.Static |
                BindingFlags.FlattenHierarchy);
        var signaturesField = typeof(AssetImporterTextureLoader)
            .GetField("SourceTypeSignatures",
                BindingFlags.NonPublic | BindingFlags.Static |
                BindingFlags.FlattenHierarchy);

        _extensions = (string[])extensionsField.GetValue(null);
        _signatures = (string[])signaturesField.GetValue(null);
    }

    public static void Postfix(ref string __result, string basePath,
                               AssetImporterTextureLoader.SourceType type)
    {
        try
        {
            // Si ya encontró la textura, no hacer nada
            if (__result != null) return;

            // Verificar si el nombre tiene sufijo de versión
            if (!VersionSuffix.IsMatch(basePath)) return;

            // Log temporal para diagnóstico
            Debug.Log("SubfolderImport FindTexture - basePath: " + basePath);
            string basePathWithoutVersion = VersionSuffix.Replace(basePath, "");
            Debug.Log("SubfolderImport FindTexture - sinVersion: " + basePathWithoutVersion);

            // Remover el sufijo de versión
           

            // Intentar encontrar la textura sin el sufijo de versión
            // Replicamos la lógica de FindTexture original
            var extensionsField = typeof(AssetImporterTextureLoader)
                .GetField("SourceTextureExtensions",
                    BindingFlags.NonPublic | BindingFlags.Static |
                    BindingFlags.FlattenHierarchy);
            var signaturesField = typeof(AssetImporterTextureLoader)
                .GetField("SourceTypeSignatures",
                    BindingFlags.NonPublic | BindingFlags.Static |
                    BindingFlags.FlattenHierarchy);

            string[] extensions = _extensions;
            string[] signatures = _signatures;

            for (int i = 0; i < extensions.Length; i++)
            {
                string candidate = basePathWithoutVersion +
                                   signatures[(int)type] +
                                   extensions[i];
                if (FileUtils.Exists(candidate))
                {
                    __result = candidate;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("SubfolderImport FindTexture patch error: " + ex);
        }
    }
}