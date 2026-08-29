using System;
using System.Collections.Generic;
using HarmonyLib;
using Lamb.UI;
using UnityEngine;

namespace CustomSpineLoader.MapEditor.WorldMap;

internal class CustomLineVisual : MonoBehaviour
{
    private Transform _lineNode;
    private MMUILineRenderer _normal;
    private MMUILineRenderer _selectable;
    private MMUILineRenderer _visited;
    private MMUILineRenderer _highlighted;

    private Vector2 _from;
    private Vector2 _to;

    public static CustomLineVisual Attach(GameObject clone)
    {
        if (clone == null) return null;

        var connection = clone.GetComponent<DLCMapConnection>();
        if (connection == null) connection = clone.GetComponentInChildren<DLCMapConnection>(true);
        if (connection == null) return null;

        var visual = clone.AddComponent<CustomLineVisual>();

        try
        {
            var reader = Traverse.Create(connection);
            visual._lineNode = reader.Field("_lineRendererNode").GetValue<Transform>();
            visual._normal = reader.Field("_normalLineRenderer").GetValue<MMUILineRenderer>();
            visual._selectable = reader.Field("_selectableLineRenderer").GetValue<MMUILineRenderer>();
            visual._visited = reader.Field("_visitedLineRenderer").GetValue<MMUILineRenderer>();
            visual._highlighted = reader.Field("_highlightedLineRenderer").GetValue<MMUILineRenderer>();
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("World map: reading the vanilla link art failed - " + e.Message);
        }

        if (visual._normal == null)
        {
            Plugin.Log.LogWarning("World map: the vanilla link clone had no line renderer; using our own art.");
            return null;
        }

        CustomMapSkin.StripLogic(clone, visual);
        if (visual._lineNode != null) visual._lineNode.gameObject.SetActive(true);
        return visual;
    }

    private IEnumerable<MMUILineRenderer> Renderers()
    {
        if (_normal != null) yield return _normal;
        if (_selectable != null) yield return _selectable;
        if (_visited != null) yield return _visited;
        if (_highlighted != null) yield return _highlighted;
    }

    public void Place(Vector2 from, Vector2 to)
    {
        _from = from;
        _to = to;

        foreach (var renderer in Renderers())
        {
            renderer.Points =
            [
                new MMUILineRenderer.BranchPoint(from),
                new MMUILineRenderer.BranchPoint(to)
            ];
            renderer.Fill = 1f;
            renderer.UpdateValues();
            renderer.UpdateRendering();
        }
    }

    public void Apply(WorldNodeState from, WorldNodeState to)
    {
        if (from == WorldNodeState.Hidden || to == WorldNodeState.Hidden)
        {
            Show(null, 0f);
            return;
        }

        if (from == WorldNodeState.Locked || to == WorldNodeState.Locked)
        {
            Show(_normal, 0.5f);
            return;
        }

        if (from == WorldNodeState.Completed && to == WorldNodeState.Completed)
        {
            Show(_visited ?? _normal, 1f);
            return;
        }

        if (from == WorldNodeState.Completed && to == WorldNodeState.Selectable)
        {
            Show(_selectable ?? _normal, 1f);
            return;
        }

        Show(_normal, to == WorldNodeState.Preview ? 0.5f : 1f);
    }

    public void ApplyEditView()
    {
        Show(_selectable ?? _normal, 1f);
    }

    private void Show(MMUILineRenderer wanted, float alpha)
    {
        if (_lineNode != null) _lineNode.gameObject.SetActive(wanted != null);
        gameObject.SetActive(wanted != null);
        if (wanted == null) return;

        foreach (var renderer in Renderers())
            renderer.gameObject.SetActive(ReferenceEquals(renderer, wanted));

        var colour = wanted.Color;
        colour.a = alpha;
        wanted.Color = colour;
        wanted.Fill = 1f;
        wanted.UpdateRendering();
    }

    private void OnEnable()
    {
        if (_normal != null && _from != _to) Place(_from, _to);
    }
}
