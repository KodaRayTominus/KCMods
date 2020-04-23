﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using TMPro;
using UnityEngine.Events;
using UnityEngine.UI;
using Zat.Shared.InterModComm;
using Zat.Shared.ModMenu.API;
using Zat.ModMenu.UI.Handlers;
using Zat.ModMenu.UI.Entries;
using Newtonsoft.Json;
using Zat.Shared.UI.Utilities;

namespace Zat.ModMenu.UI
{
    public class ModMenuUI : MonoBehaviour
    {
        public static ModMenuUI Instance { get; private set; }
        private TextMeshProUGUI header;
        private UnityEngine.UI.Button collapseExpand, reset, save, close;
        private TextMeshProUGUI saveText, resetText, noModsText, collapseExpandText;
        private GameObject content, noMods;
        private IMCPort port;
        private GameObject ui;
        private SettingsManager settings;
        private SettingsEntry[] savedSettings;
        private bool isSaving = false;
        private bool collapseExpandState = false;

        public string Header
        {
            get { return header?.text; }
            set { if (header) header.text = name; }
        }
        public UnityEngine.UI.Button.ButtonClickedEvent OnCollapseExpandClick { get { return collapseExpand?.onClick; } }
        public UnityEngine.UI.Button.ButtonClickedEvent OnResetClick { get { return reset?.onClick; } }
        public UnityEngine.UI.Button.ButtonClickedEvent OnSaveClick { get { return save?.onClick; } }

        public void Start()
        {
            try
            {
                IMCPort.helper = Loader.Helper;
                transform.name = ModSettingsNames.Objects.ModMenuName;
                header = transform.Find("ModSettingsUI/Header/Text")?.GetComponent<TextMeshProUGUI>();
                collapseExpand = transform.Find("ModSettingsUI/CollapseExpand")?.GetComponent<UnityEngine.UI.Button>();
                collapseExpandText = transform.Find("ModSettingsUI/CollapseExpand/Text")?.GetComponent<TextMeshProUGUI>();
                reset = transform.Find("ModSettingsUI/Reset")?.GetComponent<UnityEngine.UI.Button>();
                resetText = transform.Find("ModSettingsUI/Reset/Text")?.GetComponent<TextMeshProUGUI>();
                save = transform.Find("ModSettingsUI/Save")?.GetComponent<UnityEngine.UI.Button>();
                saveText = transform.Find("ModSettingsUI/Save/Text")?.GetComponent<TextMeshProUGUI>();
                close = transform.Find("ModSettingsUI/Close")?.GetComponent<UnityEngine.UI.Button>();
                content = transform.Find("ModSettingsUI/Scroll View/Viewport/Content")?.gameObject;
                noMods = transform.Find("ModSettingsUI/NoMods")?.gameObject;
                noModsText = transform.Find("ModSettingsUI/NoMods/Text")?.GetComponent<TextMeshProUGUI>();
                port = gameObject.AddComponent<IMCPort>();
                settings = new SettingsManager(content, OnUIUpdate);

                ui = transform.Find("ModSettingsUI")?.gameObject;
                Instance = this;

                var drag = header.gameObject.AddComponent<DraggableRect>();
                drag.movable = ui?.GetComponent<RectTransform>();

                close.onClick.AddListener(() => ui.SetActive(false));
                reset.onClick.AddListener(() =>
                {
                    foreach (var mod in settings.ModGameObjects)
                        port.RPC(mod, ModSettingsNames.Events.ResetIssued, 5f, null, null);
                });
                save.onClick.AddListener(() => { if (!isSaving) StartCoroutine(SaveSettingsAnim()); });
                collapseExpand.onClick.AddListener(() => ToggleCollapseExpand());

                if (gameObject.name != ModSettingsNames.Objects.ModMenuName)
                    Loader.Helper.Log($"{nameof(ModMenuUI)} is attached to \"{gameObject.name}\" instead of \"{ModSettingsNames.Objects.ModMenuName}\"!");

                header.alignment = TextAlignmentOptions.Midline;
                saveText.alignment = TextAlignmentOptions.Midline;
                resetText.alignment = TextAlignmentOptions.Midline;
                collapseExpandText.alignment = TextAlignmentOptions.Midline;
                noModsText.alignment = TextAlignmentOptions.Midline;
                noMods.SetActive(true);

                port.RegisterReceiveListener<SettingsEntry>(ModSettingsNames.Methods.UpdateSetting, UpdateSettingHandler);
                port.RegisterReceiveListener<ModConfig>(ModSettingsNames.Methods.RegisterMod, RegisterModHandler);

                savedSettings = LoadSettings();
                ui.SetActive(false);
                ToggleCollapseExpand();

                Loader.Helper.Log($"Started: [{transform.parent?.name ?? "-"}] -> [{transform.name}] -> [{nameof(ModMenuUI)}]");
            }
            catch (Exception ex)
            {
                Loader.Helper.Log($"Failed to Start {nameof(ModMenuUI)}: {ex.Message}");
                Loader.Helper.Log(ex.StackTrace);
            }
        }

        private void ToggleCollapseExpand()
        {
            SetCollapseExpand(!collapseExpandState);
        }
        private void SetCollapseExpand(bool value)
        {
            collapseExpandState = value;
            if (collapseExpandState)
                collapseExpandText.text = "Collapse all";
            else
                collapseExpandText.text = "Expand all";

            var categories = content.GetComponentsInChildren<CategoryEntry>(true);
            foreach (var cat in categories)
                cat.Expanded = collapseExpandState;
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.O))
                if (ui != null) ui.SetActive(!ui.activeSelf);
        }

        private SettingsEntry[] LoadSettings()
        {
            if (PlayerPrefs.HasKey("ModMenu"))
                return JsonConvert.DeserializeObject<SettingsEntry[]>(PlayerPrefs.GetString("ModMenu"));
            return new SettingsEntry[0];
        }
        private bool SaveSettings()
        {
            isSaving = true;
            try
            {
                PlayerPrefs.SetString("ModMenu", JsonConvert.SerializeObject(settings.Settings.ToArray()));
                PlayerPrefs.Save();
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                isSaving = false;
            }
        }
        private IEnumerator SaveSettingsAnim()
        {
            if (SaveSettings())
            {
                saveText.text = "Saved!";
                yield return new WaitForSeconds(3f);
                saveText.text = "Save";
            }
            else
            {
                saveText.text = "Error!";
            }
        }

        #region API Implementation
        private void RegisterModHandler(IRequestHandler handler, string source, ModConfig mod)
        {
            try
            {
                settings.RegisterMod(source, mod);
                var _saved = savedSettings != null ? savedSettings.Where(s => mod.settings.Any(m => m.path == s.path)).ToArray() : new SettingsEntry[0];
                handler.SendResponse(port.gameObject.name, _saved);
                LayoutRebuilder.ForceRebuildLayoutImmediate(content.GetComponent<RectTransform>());
                if (noMods != null) noMods.SetActive(!settings.Mods.Any());
                SetCollapseExpand(false);
            }
            catch(Exception ex)
            {
                handler.SendError(port.gameObject.name, ex);
            }
        }

        private void OnUIUpdate(SettingsEntry setting)
        {
            try
            {
                var associatedMods = settings.GetAssociatedMods(setting).ToArray();
                if (associatedMods.Length == 0)
                    throw new Exception($"Detected UI update for \"{setting.path}\" but it has no associated mods");

                foreach (var mod in associatedMods)
                    port.RPC(mod.GameObject, ModSettingsNames.Events.SettingChanged, setting, 5f, null, null);
            }
            catch (Exception ex)
            {
                var updateEx = new UpdateFailedException(ex.Message);
                Loader.Helper.Log(ex.Message);
                Loader.Helper.Log(ex.StackTrace);
                throw updateEx;
            }
        }

        private void UpdateSettingHandler(IRequestHandler handler, string source, SettingsEntry entry)
        {
            var context = settings.GetSettingByPath(entry.path);
            if (context == null)
            {
                handler.SendError(port.gameObject.name, $"Entry \"{entry.path}\" not registered!");
                return;
            }
            if (context.Setting.UpdateableFrom(entry))
            {
                context.UpdateSetting(entry);
                EntryHandler.Instance.UpdateEntry(context.Setting, context.UIElement);
            }
            handler.SendResponse(port.gameObject.name);
        }

        public class UpdateFailedException : Exception
        {
            public UpdateFailedException(string reason) : base($"Failed to send UI update: {reason}") { }
        }
        #endregion
    }
}
