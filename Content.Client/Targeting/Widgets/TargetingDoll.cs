using System.Numerics;
using Content.Shared.Medical.Wounds;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Shared.Input;

namespace Content.Client.Targeting.Widgets;

/// <summary>
/// The body-targeting doll HUD widget (FR-A2). Each body zone is a clickable panel laid out in the SS13
/// doll shape. The selected zone gets a highlight border, hovered zones a lighter one, and each zone is
/// tinted red by the severity of that part's own wounds (FR-A7). Clicking a zone raises
/// <see cref="OnZoneSelected"/>; the controller turns that into a predicted target-change request.
///
/// This is a texture-free approximation of the spec's RSI silhouette (base/hover/selected/damage layers):
/// it uses solid panels so the feature needs no bespoke art. Swapping in an RSI later only touches this file.
/// </summary>
public sealed class TargetingDoll : UIWidget
{
    // Presentation constants. Kept here (not hard-coded per call) so a future style pass / RSI swap is local.
    private static readonly Color BaseColor = Color.FromHex("#33333B");
    private static readonly Color DamageLow = Color.FromHex("#3d0000");  // t -> 0
    private static readonly Color DamageHigh = Color.FromHex("#ff6a6a"); // t -> 1
    private static readonly Color SelectedBorder = Color.FromHex("#4da6ff");
    private static readonly Color HoverBorder = Color.FromHex("#ffffffcc");

    private readonly Dictionary<WoundBodyPart, DollZone> _zones = new();

    /// <summary>Raised with the clicked zone. The controller validates + sends the predicted change.</summary>
    public Action<WoundBodyPart>? OnZoneSelected;

    public TargetingDoll()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalAlignment = HAlignment.Right;
        VerticalAlignment = VAlignment.Bottom;
        // Sit above the hotbar / hands row.
        Margin = new Thickness(0, 0, 8, 210);

        AddRow(WoundBodyPart.Head);
        AddRow(WoundBodyPart.RightHand, WoundBodyPart.RightArm, WoundBodyPart.Chest, WoundBodyPart.LeftArm, WoundBodyPart.LeftHand);
        AddRow(WoundBodyPart.RightLeg, WoundBodyPart.LeftLeg);
        AddRow(WoundBodyPart.RightFoot, WoundBodyPart.LeftFoot);
    }

    private void AddRow(params WoundBodyPart[] parts)
    {
        var row = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
        };

        foreach (var part in parts)
        {
            var zone = new DollZone(part);
            zone.Clicked += p => OnZoneSelected?.Invoke(p);
            _zones[part] = zone;
            row.AddChild(zone);
        }

        AddChild(row);
    }

    /// <summary>Marks the given zone as the selected one, clearing the highlight on all others.</summary>
    public void SetSelected(WoundBodyPart selected)
    {
        foreach (var (part, zone) in _zones)
            zone.SetSelected(part == selected);
    }

    /// <summary>
    /// Applies the red damage tint for a zone from its total wound severity, quantised into 5 discrete
    /// steps so a worsening injury reads as a distinct change rather than a smooth gradient (FR-A7).
    /// </summary>
    public void SetZoneDamage(WoundBodyPart part, float severity, float displayCap)
    {
        if (!_zones.TryGetValue(part, out var zone))
            return;

        if (severity <= 0 || displayCap <= 0)
        {
            zone.SetTint(BaseColor);
            return;
        }

        var t = Math.Clamp(severity / displayCap, 0f, 1f);
        // Quantise up to the nearest of 0.2/0.4/0.6/0.8/1.0.
        var stepped = Math.Clamp(MathF.Ceiling(t / 0.2f) * 0.2f, 0.2f, 1f);
        zone.SetTint(Color.InterpolateBetween(DamageLow, DamageHigh, stepped));
    }

    public void SetZoneTooltip(WoundBodyPart part, string? tooltip)
    {
        if (_zones.TryGetValue(part, out var zone))
            zone.ToolTip = tooltip;
    }

    /// <summary>A single clickable body-zone cell.</summary>
    private sealed class DollZone : PanelContainer
    {
        public readonly WoundBodyPart Zone;
        public event Action<WoundBodyPart>? Clicked;

        private readonly StyleBoxFlat _box = new() { BackgroundColor = BaseColor };
        private bool _selected;
        private bool _hovered;

        public DollZone(WoundBodyPart zone)
        {
            Zone = zone;
            MinSize = new Vector2(26, 26);
            Margin = new Thickness(1);
            MouseFilter = MouseFilterMode.Stop;
            PanelOverride = _box;

            OnMouseEntered += _ =>
            {
                _hovered = true;
                UpdateBorder();
            };
            OnMouseExited += _ =>
            {
                _hovered = false;
                UpdateBorder();
            };
        }

        public void SetTint(Color color)
        {
            _box.BackgroundColor = color;
        }

        public void SetSelected(bool selected)
        {
            _selected = selected;
            UpdateBorder();
        }

        // Selection is the top layer and always wins over hover, so the selected zone stays readable at any
        // damage level (FR-A7).
        private void UpdateBorder()
        {
            if (_selected)
            {
                _box.BorderColor = SelectedBorder;
                _box.BorderThickness = new Thickness(2);
            }
            else if (_hovered)
            {
                _box.BorderColor = HoverBorder;
                _box.BorderThickness = new Thickness(1);
            }
            else
            {
                _box.BorderColor = Color.Transparent;
                _box.BorderThickness = new Thickness(0);
            }
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);

            if (args.Function != EngineKeyFunctions.UIClick)
                return;

            Clicked?.Invoke(Zone);
            args.Handle();
        }
    }
}
