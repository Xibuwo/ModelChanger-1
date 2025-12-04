using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using Bulbul;
using ChillWithYou.ModelChanger.Patches;

namespace ChillWithYou.ModelChanger
{
    public class ModelChangerUI : MonoBehaviour
    {
        private GameObject _menuPanel;
        private bool _isVisible = false;
        private string _pendingModel = null;

        private void Start()
        {
            try
            {
                CreateMenu();
                ModelChangerPlugin.Log?.LogInfo("ModelChanger UI initialized successfully");
            }
            catch (System.Exception ex)
            {
                ModelChangerPlugin.Log?.LogError($"Failed to create UI: {ex.Message}");
                ModelChangerPlugin.Log?.LogError($"Stack trace: {ex.StackTrace}");
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(ModelChangerPlugin.Cfg_MenuToggleKey.Value))
            {
                ToggleMenu();
            }
        }

        private void ToggleMenu()
        {
            _isVisible = !_isVisible;
            if (_menuPanel != null)
            {
                _menuPanel.SetActive(_isVisible);
            }

            // Pause game
            Time.timeScale = _isVisible ? 0f : 1f;
        }

        private void CreateMenu()
        {
            // Create canvas
            var canvas = new GameObject("ModelChangerCanvas");
            canvas.transform.SetParent(transform);

            var canvasComponent = canvas.AddComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasComponent.sortingOrder = 9999;

            canvas.AddComponent<CanvasScaler>();
            canvas.AddComponent<GraphicRaycaster>();

            // Main panel
            _menuPanel = CreatePanel(canvas.transform);
            _menuPanel.SetActive(false);

            // Title
            CreateText(_menuPanel.transform, "Model Changer", new Vector2(0, 220), 32);

            // Status text
            var statusText = CreateText(_menuPanel.transform, "Current: " + ModelChangerPlugin.Cfg_CurrentModel.Value, new Vector2(0, 180), 18);
            statusText.name = "StatusText";

            // Apply button
            var applyBtn = CreateButton(_menuPanel.transform, "Apply Selected Model", Vector2.zero, () => ApplyModel(), false);
            var applyRect = applyBtn.GetComponent<RectTransform>();
            applyRect.anchorMin = new Vector2(0.5f, 0);
            applyRect.anchorMax = new Vector2(0.5f, 0);
            applyRect.sizeDelta = new Vector2(300, 50);
            applyRect.anchoredPosition = new Vector2(0, 30);

            // Model List
            CreateScroller(_menuPanel.transform);
        }

        private void ApplyModel()
        {
            if (_pendingModel == null || _pendingModel == ModelChangerPlugin.Cfg_CurrentModel.Value)
            {
                ModelChangerPlugin.Log?.LogInfo("No new model selected");
                return;
            }

            // Save to config
            ModelChangerPlugin.Cfg_CurrentModel.Value = _pendingModel;

            // Find the HeroineService and force reload
            var heroineService = FindObjectOfType<HeroineService>();
            if (heroineService != null)
            {
                ModelChangerPlugin.Log?.LogInfo($"Applying model change to: {_pendingModel}");

                // Destroy current custom model if any
                if (ModelChangerPlugin.CurrentCharacterObject != null)
                {
                    Destroy(ModelChangerPlugin.CurrentCharacterObject);
                    ModelChangerPlugin.CurrentCharacterObject = null;
                }

                // Re-enable original model if going back to default
                if (_pendingModel == "Default")
                {
                    foreach (var renderer in heroineService.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.gameObject != heroineService.gameObject)
                        {
                            renderer.enabled = true;
                        }
                    }
                }
                else
                {
                    // Load the new custom model
                    var modelData = ModelChangerPlugin.ModelRegistry.GetModel(_pendingModel);
                    if (modelData != null && modelData.IsCustom)
                    {
                        // This will be handled by the patch logic
                        HeroineModelSwapperPatch.ApplyModelToCharacter(heroineService.gameObject, modelData);
                    }
                }

                // Update status text
                var statusText = _menuPanel.transform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
                if (statusText != null)
                {
                    statusText.text = "Current: " + _pendingModel;
                }

                ModelChangerPlugin.Log?.LogInfo("Model applied successfully!");
            }
            else
            {
                ModelChangerPlugin.Log?.LogWarning("HeroineService not found in scene");
            }
        }

        private void CreateScroller(Transform parent)
        {
            var scrollRectGO = new GameObject("ModelScrollRect");
            scrollRectGO.transform.SetParent(parent, false);

            var rect = scrollRectGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400, 300);
            rect.anchoredPosition = new Vector2(0, -20);

            var scrollRect = scrollRectGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 20f;

            // Viewport
            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollRectGO.transform, false);
            var viewportRect = viewportGO.AddComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.sizeDelta = Vector2.zero;
            viewportGO.AddComponent<RectMask2D>();
            scrollRect.viewport = viewportRect;

            // Content Container
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);

            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 5;

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRect;

            // Create buttons
            foreach (var model in ModelChangerPlugin.ModelRegistry.GetAllModels())
            {
                CreateModelButton(contentRect, model.Name);
            }
        }

        private void CreateModelButton(Transform parent, string modelName)
        {
            var buttonObj = new GameObject("Button_" + modelName);
            buttonObj.transform.SetParent(parent, false);

            var layoutElement = buttonObj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 40;
            layoutElement.preferredHeight = 40;
            layoutElement.minWidth = 380;

            var button = buttonObj.AddComponent<Button>();
            var image = buttonObj.AddComponent<Image>();

            // Highlight if current or pending
            bool isCurrent = ModelChangerPlugin.Cfg_CurrentModel.Value == modelName;
            bool isPending = _pendingModel == modelName;

            if (isCurrent && isPending)
                image.color = new Color(0.1f, 0.5f, 0.1f, 1f); // Green = current & selected
            else if (isPending)
                image.color = new Color(0.3f, 0.3f, 0.5f, 1f); // Blue = pending
            else if (isCurrent)
                image.color = new Color(0.2f, 0.4f, 0.2f, 1f); // Dark green = current
            else
                image.color = new Color(0.2f, 0.2f, 0.2f, 1f); // Gray = normal

            button.onClick.AddListener(() => SelectModelForApply(modelName));

            // Button Text
            var textObj = new GameObject("ButtonText");
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = modelName + (isCurrent ? " ✓" : "");
            tmp.fontSize = 24;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        private void SelectModelForApply(string modelName)
        {
            _pendingModel = modelName;
            ModelChangerPlugin.Log?.LogInfo($"Selected: {modelName} (press Apply to change)");

            // Refresh the button colors
            RefreshButtonColors();
        }

        private void RefreshButtonColors()
        {
            var content = _menuPanel.transform.Find("ModelScrollRect/Viewport/Content");
            if (content == null) return;

            foreach (Transform child in content)
            {
                var button = child.GetComponent<Button>();
                var image = child.GetComponent<Image>();
                if (button == null || image == null) continue;

                string btnModelName = child.name.Replace("Button_", "");
                bool isCurrent = ModelChangerPlugin.Cfg_CurrentModel.Value == btnModelName;
                bool isPending = _pendingModel == btnModelName;

                if (isCurrent && isPending)
                    image.color = new Color(0.1f, 0.5f, 0.1f, 1f);
                else if (isPending)
                    image.color = new Color(0.3f, 0.3f, 0.5f, 1f);
                else if (isCurrent)
                    image.color = new Color(0.2f, 0.4f, 0.2f, 1f);
                else
                    image.color = new Color(0.2f, 0.2f, 0.2f, 1f);

                // Update checkmark
                var textComp = child.GetComponentInChildren<TextMeshProUGUI>();
                if (textComp != null)
                {
                    textComp.text = btnModelName + (isCurrent ? " ✓" : "");
                }
            }
        }

        private GameObject CreatePanel(Transform parent)
        {
            var panelObj = new GameObject("Panel");
            panelObj.transform.SetParent(parent, false);

            var rect = panelObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(500, 550);

            var image = panelObj.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            return panelObj;
        }

        private GameObject CreateText(Transform parent, string text, Vector2 position, int fontSize)
        {
            var textObj = new GameObject("Text");
            textObj.transform.SetParent(parent, false);

            var rect = textObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400, 50);
            rect.anchoredPosition = position;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return textObj;
        }

        private GameObject CreateButton(Transform parent, string text, Vector2 position, UnityEngine.Events.UnityAction onClick, bool useLayout = true)
        {
            var buttonObj = new GameObject("Button");
            buttonObj.transform.SetParent(parent, false);

            if (!useLayout)
            {
                var rect = buttonObj.AddComponent<RectTransform>();
                rect.anchoredPosition = position;
            }

            var button = buttonObj.AddComponent<Button>();
            var image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.5f, 0.2f, 1f);
            button.onClick.AddListener(onClick);

            var textObj = new GameObject("ButtonText");
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return buttonObj;
        }
    }
}