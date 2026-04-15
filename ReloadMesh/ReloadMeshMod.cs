using CitiesHarmony.API;
using ColossalFramework.UI;
using HarmonyLib;
using ICities;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReloadMesh
{
    

    // Clase estática que guarda el estado del último import
    public static class ReloadState
    {
        public static string LastImportPath;
        public static string LastImportFilename;
        public static ImportAsset LastImportAsset;
    }

    // Patch para guardar la referencia cuando se llama Import()
    [HarmonyPatch(typeof(ImportAsset), "Import",
    new Type[] { typeof(string), typeof(string) })]
    public class ImportAssetPatch
    {
        public static void Prefix(ImportAsset __instance, string path, string filename)
        {
            try
            {
                ReloadState.LastImportPath = path;
                ReloadState.LastImportFilename = filename;
                ReloadState.LastImportAsset = __instance;

                // Habilitar el botón ahora que tenemos un asset
                if (DecorationPropertiesPanelPatch.ReloadButton != null)
                {
                    DecorationPropertiesPanelPatch.ReloadButton.isEnabled = true;
                    DecorationPropertiesPanelPatch.ReloadButton.tooltip =
                        "Reload mesh from Import folder";
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("ReloadMesh ImportAsset patch error: " + ex);
            }
        }
    }

    // Patch para agregar el botón al DecorationPropertiesPanel
    [HarmonyPatch(typeof(DecorationPropertiesPanel), "OnEditPrefabChanged")]
    public class DecorationPropertiesPanelPatch
    {
        public static ColossalFramework.UI.UIButton ReloadButton;


        public static void Postfix(DecorationPropertiesPanel __instance, PrefabInfo info)
        {
            try
            {
                if (info == null) return;

                // Verificar si el asset actual corresponde al último importado
                bool isImportedAsset = ReloadState.LastImportAsset != null &&
                    ReloadState.LastImportAsset.Object != null &&
                    ReloadState.LastImportAsset.Object.GetComponent<PrefabInfo>() == info;

                // Si el botón ya existe, solo actualizar su estado
                if (ReloadButton != null)
                {
                    ReloadButton.isEnabled = isImportedAsset;
                    ReloadButton.tooltip = isImportedAsset
                        ? "Reload imported mesh and textures"
                        : "Nothing to reload, make sure to import files from /Import folder";
                    return;
                }

                // Crear el botón usando m_Container
                var containerField = typeof(DecorationPropertiesPanel)
                    .GetField("m_Container",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                UIScrollablePanel container = (UIScrollablePanel)containerField.GetValue(__instance);
                UIComponent targetPanel = (UIComponent)container;

                var templateField = typeof(DecorationPropertiesPanel)
                    .GetField("kPropertyButtonTemplate",
                        BindingFlags.NonPublic | BindingFlags.Static);
                string buttonTemplate = (string)templateField.GetValue(null);

                UIComponent buttonComponent = targetPanel.AttachUIComponent(
                    UITemplateManager.GetAsGameObject(buttonTemplate));

                ReloadButton = buttonComponent.Find<ColossalFramework.UI.UIButton>("Button");
                ReloadButton.text = "Reload Mesh";
                ReloadButton.width = 120f;
                ReloadButton.height = 30f;
                ReloadButton.textScale = 0.8f;
                ReloadButton.isEnabled = isImportedAsset;
                ReloadButton.tooltip = isImportedAsset
                    ? "Reload imported mesh and textures"
                    : "No file to reload, use files from /Import folder";

                ReloadButton.eventClicked += (c, e) =>
                {
                    __instance.StartCoroutine(ReloadCoroutine(__instance));
                };

                Debug.Log("ReloadMesh: button created successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError("ReloadMesh OnEditPrefabChanged error: " + ex.Message);
                Debug.LogError("ReloadMesh stacktrace: " + ex.StackTrace);
            }
        }

        private static IEnumerator ReloadCoroutine(DecorationPropertiesPanel panel)
        {
            if (ReloadState.LastImportAsset == null)
            {
                Debug.LogError("ReloadMesh: No import asset found");
                yield break;
            }

            if (string.IsNullOrEmpty(ReloadState.LastImportPath) ||
                string.IsNullOrEmpty(ReloadState.LastImportFilename))
            {
                Debug.LogError("ReloadMesh: No import path found");
                yield break;
            }

            // Reimportar el FBX
            ReloadState.LastImportAsset.Import(
                ReloadState.LastImportPath,
                ReloadState.LastImportFilename);

            // Esperar a que el modelo esté listo
            while (!ReloadState.LastImportAsset.ReadyForEditing)
            {
                yield return null;
            }

            // Esperar a que las texturas terminen de cargar
            while (ReloadState.LastImportAsset.IsLoadingTextures)
            {
                yield return null;
            }
            Debug.Log("ReloadMesh: Textures loaded, calling FinalizeImport");
            // Finalizar el import — comprime texturas y genera thumbnails
            ReloadState.LastImportAsset.FinalizeImport();
            
            // Esperar tasks de compresión frame por frame
            Renderer renderer = ReloadState.LastImportAsset.Object?.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                Material mat = renderer.material;
                foreach (string texName in new[] { "_MainTex", "_XYSMap", "_ACIMap" })
                {
                    Texture2D tex = mat.GetTexture(texName) as Texture2D;
                    Debug.Log($"ReloadMesh: {texName} = {(tex != null ? tex.format.ToString() : "NULL")}");
                }
            }

            // Actualizar el prefab en el editor
            ToolsModifierControl.toolController.m_editPrefabInfo =
                ReloadState.LastImportAsset.Object.GetComponent<PrefabInfo>();

            Debug.Log("ReloadMesh: Mesh reloaded successfully");
        }
    }
}