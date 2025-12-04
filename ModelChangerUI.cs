using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

namespace ChillWithYou.ModelChanger
{
    public class ModelChangerUI : MonoBehaviour
    {
        private GameObject _menuPanel;
        private bool _isVisible = false;

        private void Start()
        {
            CreateMenu();
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

            // Pause game (Freeze function kept as requested)
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
            CreateText(_menuPanel.transform, "Model Changer", new Vector2(0, 200), 32);

            // Model List
            CreateScroller(_menuPanel.transform);
        }

        private void CreateScroller(Transform parent)
        {
            var scrollRectGO = new GameObject("ModelScrollRect");
            scrollRectGO.transform.SetParent(parent, false);

            var rect = scrollRectGO.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(400, 300);
            rect.anchoredPosition = new Vector2(0, -50);

            var scrollRect = scrollRectGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 20f;

            // Viewport (Masking)
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

            // Vertical Layout Group - Automatically handles positioning
            var layout = contentGO.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false; // Prevents buttons from stretching weirdly
            layout.spacing = 5;

            // Content Size Fitter - Resizes content container based on number of buttons
            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = contentRect;

            // Create buttons (Position is now handled automatically)
            foreach (var model in ModelChangerPlugin.ModelRegistry.GetAllModels())
            {
                CreateButton(contentRect, model.Name, () => SelectModel(model.Name));
            }
        }

        private void SelectModel(string modelName)
        {
            ModelChangerPlugin.Cfg_CurrentModel.Value = modelName;
            ModelChangerPlugin.Log?.LogInfo($"Model set to: {modelName}. Switch scene or reload to apply.");
        }

        private GameObject CreatePanel(Transform parent)
        {
            var panelObj = new GameObject("Panel");
            panelObj.transform.SetParent(parent, false);

            var rect = panelObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(500, 500);

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

        private void CreateButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
        {
            var buttonObj = new GameObject("Button");
            buttonObj.transform.SetParent(parent, false);

            // Layout Element - Tells the VerticalLayoutGroup how tall this button should be
            var layoutElement = buttonObj.AddComponent<LayoutElement>();
            layoutElement.minHeight = 40;
            layoutElement.preferredHeight = 40;
            layoutElement.minWidth = 380;

            var button = buttonObj.AddComponent<Button>();
            var image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 1f);
            button.onClick.AddListener(onClick);

            // Button Text
            var textObj = new GameObject("ButtonText");
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            textRect.offsetMin = new Vector2(10, 0); // Add some padding
            textRect.offsetMax = new Vector2(-10, 0);

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 24;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            // Highlight selected
            if (ModelChangerPlugin.Cfg_CurrentModel.Value == text)
            {
                image.color = new Color(0.1f, 0.4f, 0.1f, 1f);
            }
        }
    }
}