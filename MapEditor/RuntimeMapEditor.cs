using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CustomSpineLoader.MapEditor
{
    // Runtime in-game Sprite Shape editor. Toggle UI with F4 from Plugin.
    public class RuntimeMapEditor : MonoBehaviour
    {
        Canvas canvas;
        RectTransform panel;
        Camera mainCam;

        object shapeController;
        Type shapeControllerType;

        List<Vector3> controlPoints = new List<Vector3>();
        List<GameObject> handles = new List<GameObject>();

        void Awake()
        {
            mainCam = Camera.main;
            CreateUi();
            HideUi();
        }

        void Update()
        {
            if (canvas != null && canvas.enabled)
            {
                // Keep handle screen positions synced with world points
                UpdateHandlePositions();
            }
        }

        public void ToggleEditor()
        {
            if (canvas != null) ToggleUi();
        }

        void ToggleUi()
        {
            canvas.enabled = !canvas.enabled;
            if (canvas.enabled) EnterEditorMode();
            else ExitEditorMode();
        }

        void CreateUi()
        {
            var go = new GameObject("RuntimeMapEditor_Canvas");
            DontDestroyOnLoad(go);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            // Panel
            var panelGO = new GameObject("EditorPanel");
            panelGO.transform.SetParent(canvas.transform, false);
            panel = panelGO.AddComponent<RectTransform>();
            panel.sizeDelta = new Vector2(420, 220);
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0, -10);

            var img = panelGO.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.6f);

            // Title
            var titleGO = CreateText("Editor Mode", 20, TextAnchor.UpperCenter);
            titleGO.transform.SetParent(panel, false);
            var rt = titleGO.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(0, -10);

            // Controls container for buttons
            var btnAdd = CreateButton("Add Point", new Vector2(-140, -40), () => AddPointAtMouse());
            btnAdd.transform.SetParent(panel, false);
            var btnCenter = CreateButton("Center On Shape", new Vector2(0, -40), () => CenterOnShape());
            btnCenter.transform.SetParent(panel, false);
            var btnClose = CreateButton("Close (F4)", new Vector2(140, -40), () => ToggleUi());
            btnClose.transform.SetParent(panel, false);

            // Info
            var info = CreateText("Drag handles to edit shape. Add/Delete points.", 14, TextAnchor.UpperLeft);
            info.transform.SetParent(panel, false);
            var infoRt = info.GetComponent<RectTransform>();
            infoRt.anchoredPosition = new Vector2(-180, -80);

            canvas.enabled = false;
        }

        GameObject CreateButton(string text, Vector2 anchoredPos, Action onClick)
        {
            var go = new GameObject("Btn_" + text);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(140, 28);
            rt.anchoredPosition = anchoredPos;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.2f, 0.2f, 0.95f);
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(() => onClick?.Invoke());

            var txt = CreateText(text, 14, TextAnchor.MiddleCenter);
            txt.transform.SetParent(go.transform, false);
            return go;
        }

        GameObject CreateText(string content, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text_" + content);
            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = size;
            txt.color = Color.white;
            txt.alignment = anchor;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(380, 30);
            return go;
        }

        void ShowUi() => canvas.enabled = true;
        void HideUi() => canvas.enabled = false;

        void EnterEditorMode()
        {
            FindShapeController();
            if (shapeController == null)
            {
                Debug.LogWarning("RuntimeMapEditor: No SpriteShapeController found in scene.");
                return;
            }

            LoadControlPointsFromShape();
            CreateHandles();
        }

        void ExitEditorMode()
        {
            ClearHandles();
        }

        void FindShapeController()
        {
            var all = FindObjectsOfType<MonoBehaviour>();
            foreach (var obj in all)
            {
                if (obj == null) continue;
                var t = obj.GetType();
                if (t.Name == "SpriteShapeController")
                {
                    shapeController = obj;
                    shapeControllerType = t;
                    break;
                }
            }
        }

        void LoadControlPointsFromShape()
        {
            controlPoints.Clear();
            if (shapeController == null || shapeControllerType == null) return;

            var splineProp = shapeControllerType.GetProperty("spline", BindingFlags.Public | BindingFlags.Instance);
            if (splineProp == null) return;

            var spriteShape = splineProp.GetValue(shapeController);
            if (spriteShape == null) return;

            var getPointCount = spriteShape.GetType().GetMethod("GetPointCount");
            var getPosition = spriteShape.GetType().GetMethod("GetPosition");
            if (getPointCount == null || getPosition == null) return;

            int pointCount = (int)getPointCount.Invoke(spriteShape, null);
            for (int i = 0; i < pointCount; i++)
            {
                var pos = getPosition.Invoke(spriteShape, new object[] { i });
                if (pos is Vector3 v) controlPoints.Add(v);
                else if (pos is Vector2 v2) controlPoints.Add(new Vector3(v2.x, v2.y, 0));
            }
        }

        void CreateHandles()
        {
            ClearHandles();
            for (int i = 0; i < controlPoints.Count; i++)
            {
                var h = CreateHandle(i);
                handles.Add(h);
            }
        }

        GameObject CreateHandle(int index)
        {
            var go = new GameObject("Handle_" + index);
            go.transform.SetParent(canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(18, 18);
            var img = go.AddComponent<Image>();
            img.color = Color.cyan;

            var ph = go.AddComponent<PointHandle>();
            ph.Initialize(this, index);

            // Delete button overlay
            var del = new GameObject("Del");
            del.transform.SetParent(go.transform, false);
            var drt = del.AddComponent<RectTransform>();
            drt.anchorMin = new Vector2(1, 1);
            drt.anchorMax = new Vector2(1, 1);
            drt.anchoredPosition = new Vector2(6, 6);
            drt.sizeDelta = new Vector2(12, 12);
            var dimg = del.AddComponent<Image>();
            dimg.color = Color.red;
            var dbtn = del.AddComponent<Button>();
            int idx = index;
            dbtn.onClick.AddListener(() => RemovePoint(idx));

            return go;
        }

        void UpdateHandlePositions()
        {
            for (int i = 0; i < handles.Count; i++)
            {
                if (i >= controlPoints.Count) continue;
                var worldPos = controlPoints[i];
                Vector3 screen = mainCam.WorldToScreenPoint(worldPos);
                var rt = handles[i].GetComponent<RectTransform>();
                rt.position = screen;
            }
        }

        void ClearHandles()
        {
            foreach (var h in handles) if (h != null) Destroy(h);
            handles.Clear();
        }

        public void SetPointPosition(int index, Vector3 worldPos)
        {
            if (shapeController == null || shapeControllerType == null || index < 0 || index >= controlPoints.Count) return;
            controlPoints[index] = worldPos;
            var spriteShape = GetSpriteShape();
            var setPosition = spriteShape?.GetType().GetMethod("SetPosition");
            if (setPosition != null)
            {
                setPosition.Invoke(spriteShape, new object[] { index, worldPos });
                ApplyShapeUpdates();
            }
        }

        public void RemovePoint(int index)
        {
            if (shapeController == null || shapeControllerType == null || index < 0 || index >= controlPoints.Count) return;
            var spriteShape = GetSpriteShape();
            var removePoint = spriteShape?.GetType().GetMethod("RemovePointAt");
            if (removePoint != null)
            {
                removePoint.Invoke(spriteShape, new object[] { index });
                controlPoints.RemoveAt(index);
                ApplyShapeUpdates();
                CreateHandles();
            }
        }

        public void AddPointAtMouse()
        {
            if (shapeController == null || shapeControllerType == null) return;
            Vector3 mouse = Input.mousePosition;
            if (mainCam == null) mainCam = Camera.main;
            var planeZ = -mainCam.transform.position.z;
            var ray = mainCam.ScreenPointToRay(mouse);
            var point = ray.GetPoint(planeZ / ray.direction.z);
            var spriteShape = GetSpriteShape();
            if (spriteShape == null) return;

            var insertPoint = spriteShape.GetType().GetMethod("InsertPointAt");
            if (insertPoint != null)
            {
                var insertIndex = controlPoints.Count;
                insertPoint.Invoke(spriteShape, new object[] { insertIndex, point });
                controlPoints.Add(point);
                ApplyShapeUpdates();
                CreateHandles();
            }
        }

        void CenterOnShape()
        {
            if (shapeController == null) return;
            var mt = (shapeController as Component)?.transform;
            if (mainCam != null && mt != null)
            {
                mainCam.transform.position = new Vector3(mt.position.x, mt.position.y, mainCam.transform.position.z);
            }
        }

        object GetSpriteShape()
        {
            if (shapeController == null || shapeControllerType == null) return null;
            var splineProp = shapeControllerType.GetProperty("spline", BindingFlags.Public | BindingFlags.Instance);
            return splineProp?.GetValue(shapeController);
        }

        void ApplyShapeUpdates()
        {
            if (shapeController == null || shapeControllerType == null) return;

            var refreshMethod = shapeControllerType.GetMethod("RefreshSpriteShape")
                ?? shapeControllerType.GetMethod("UpdateSpriteShape")
                ?? shapeControllerType.GetMethod("GenerateGeometry");
            if (refreshMethod != null)
            {
                refreshMethod.Invoke(shapeController, null);
            }

            var colliderType = shapeControllerType.Assembly.GetType("UnityEngine.U2D.SpriteShapeCollider2D");
            var collider2D = (shapeController as Component)?.GetComponent(colliderType);
            if (collider2D != null)
            {
                var updateCollider = shapeControllerType.GetMethod("BakeCollider")
                    ?? shapeControllerType.GetMethod("UpdateCollider")
                    ?? shapeControllerType.GetMethod("GenerateCollider");
                if (updateCollider != null)
                {
                    updateCollider.Invoke(shapeController, null);
                }
            }
        }
    }

    // Handles pointer drag on a UI element and notifies the editor
    public class PointHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        int index;
        RuntimeMapEditor editor;

        public void Initialize(RuntimeMapEditor e, int idx)
        {
            editor = e;
            index = idx;
        }

        public void OnPointerDown(PointerEventData eventData) { }

        public void OnDrag(PointerEventData eventData)
        {
            if (editor == null) return;
            var cam = Camera.main;
            Vector3 screen = eventData.position;
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));
            editor.SetPointPosition(index, world);
        }

        public void OnPointerUp(PointerEventData eventData) { }
    }
}
