using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using ConvenientNote.DesktopPet.Domain;

namespace ConvenientNote.DesktopPet.UI;

/// <summary>Articulated vector artwork: the cycle, feet, scarf and head share one animation clock.</summary>
public sealed class PelicanVisual : FrameworkElement
{
    private static readonly ConcurrentDictionary<string, Brush> Brushes = new();
    private static readonly ConcurrentDictionary<(Brush, double), Pen> Pens = new();
    private static readonly Brush Ink = Brush("#254E42"), Cream = Brush("#FFF9E4"), Shade = Brush("#E2E6D2");
    private static readonly Brush Gold = Brush("#F4B541"), Orange = Brush("#E99329"), Scarf = Brush("#CF6242"), Green = Brush("#286353");
    private static readonly Pen Outline = Pen(Ink, 2.4);
    private readonly Dictionary<string, Geometry> _paths = new();
    public PetAction Action { get; set; }
    public double Phase { get; set; }
    public double ActionAge { get; set; }
    public int Direction { get; set; } = 1;

    public void Update(PetAction action, double phase, double age, int direction)
    {
        Action = action; Phase = phase; ActionAge = age; Direction = direction;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Reporting the full design size forces WPF to arrange an oversized element
        // and clip it when the transparent pet window is smaller than the artwork.
        var scale = Math.Min(1, Math.Min(availableSize.Width / 360, availableSize.Height / 310));
        return new Size(360 * scale, 310 * scale);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scale = Math.Min(ActualWidth / 360, ActualHeight / 310);
        dc.PushTransform(new TranslateTransform((ActualWidth - 360 * scale) / 2, (ActualHeight - 310 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        if (Direction < 0) dc.PushTransform(new ScaleTransform(-1, 1, 180, 155));
        var riding = Action is PetAction.Ride or PetAction.Boost or PetAction.Brake;
        var pedal = riding ? Phase : 0;
        var bob = riding ? Math.Sin(pedal * 2) * 1.2 : Action == PetAction.Sleep ? Math.Sin(Phase) * 1.4 : 0;
        var tilt = Action == PetAction.Drag ? Math.Sin(Phase * 1.8) * 4 : 0;
        dc.PushTransform(new RotateTransform(tilt, 180, 155));
        dc.PushTransform(new TranslateTransform(0, Action == PetAction.Drag ? -6 : 0));

        Wheel(dc, new Point(78, 245), pedal);
        Wheel(dc, new Point(282, 245), pedal);
        // Far leg sits behind the frame. Feet remain attached to opposing pedal phases.
        var crank = new Point(179, 235);
        var farFoot = new Point(crank.X - Math.Cos(pedal) * 19, crank.Y - Math.Sin(pedal) * 19);
        var nearFoot = new Point(crank.X + Math.Cos(pedal) * 19, crank.Y + Math.Sin(pedal) * 19);
        if (Action == PetAction.Drag) { farFoot = new(170 + Math.Sin(Phase * 2) * 7, 250); nearFoot = new(204 + Math.Sin(Phase * 2 + 1) * 7, 254); }
        if (Action == PetAction.React) nearFoot = new(177, 292);
        Leg(dc, new Point(155, 163 + bob), farFoot, Brush("#D99430"));

        // Bicycle chassis, fork, chain guard, saddle and handlebar.
        Line(dc, new(78, 245), new(137, 178), Green, 7);
        Line(dc, new(137, 178), crank, Green, 7);
        Line(dc, crank, new(78, 245), Green, 7);
        Line(dc, new(137, 178), new(248, 178), Green, 7);
        Line(dc, new(248, 178), crank, Green, 7);
        Line(dc, new(241, 153), new(282, 245), Green, 7);
        Line(dc, new(137, 178), new(129, 161), Green, 6);
        Shape(dc, "M 73,239 C 104,226 149,223 180,227 L 188,242 C 150,252 103,253 74,250 Z", Green);
        dc.DrawEllipse(Cream, Outline, crank, 14, 14);
        for (var i = 0; i < 5; i++)
        {
            var a = pedal + i * Math.PI * 2 / 5;
            dc.DrawEllipse(Ink, null, new(crank.X + Math.Cos(a) * 8, crank.Y + Math.Sin(a) * 8), 1.3, 1.3);
        }
        Line(dc, farFoot, nearFoot, Orange, 4);
        Line(dc, new(116, 165), new(151, 165), Ink, 10);
        Shape(dc, "M 241,156 Q 244,144 270,146", null, Pen(Green, 6));
        Line(dc, new(260, 146), new(276, 146), Brush("#AF6D38"), 8);
        Shape(dc, "M 272,147 Q 302,154 281,203", null, Pen(Green, 1.3));
        if (Action == PetAction.Sleep) Line(dc, new(203, 226), new(218, 296), Green, 4);
        Leg(dc, new Point(148, 167 + bob), nearFoot, Gold);

        dc.PushTransform(new TranslateTransform(0, bob));
        var bodyLean = Action == PetAction.Boost ? 10 : Action == PetAction.Brake ? -5 * Math.Max(0, 1 - ActionAge / .5) : 0;
        dc.PushTransform(new RotateTransform(bodyLean, 152, 162));
        Shape(dc, "M 88,148 Q 68,136 56,110 Q 72,119 94,127 Z", Cream);
        Shape(dc, "M 84,145 C 84,113 119,105 149,119 C 170,130 198,129 213,143 C 236,163 208,186 177,190 C 140,196 105,182 84,145 Z", Cream);
        Shape(dc, "M 99,160 Q 128,180 171,181 Q 198,180 216,163 Q 199,192 169,190 Q 128,191 99,160 Z", Shade, null);

        var headTilt = Action == PetAction.Sleep ? 48 : Action == PetAction.React ? -12 * Math.Sin(Math.Min(ActionAge, 1.4) / 1.4 * Math.PI) : 0;
        var headRotation = new RotateTransform(headTilt, 200, 99);
        // The tail shares the collar's attachment points; its free end can still flutter.
        var wave = Math.Sin(Phase * 1.7) * (Action == PetAction.Sleep ? 1 : 5);
        var drop = Action is PetAction.Sleep or PetAction.Drag ? 30 : Action == PetAction.Brake ? -16 : 0;
        var scarfGeometry = new StreamGeometry();
        using (var c = scarfGeometry.Open())
        {
            var upperRoot = headRotation.Transform(new Point(185, 101));
            var lowerRoot = headRotation.Transform(new Point(183, 110));
            c.BeginFigure(upperRoot, true, true);
            c.BezierTo(new(upperRoot.X - 25, upperRoot.Y + 4), new(133, 85 + wave + drop), new(110, 87 + drop), true, false);
            c.BezierTo(new(116, 104 + drop), new(lowerRoot.X - 25, lowerRoot.Y + 12), lowerRoot, true, false);
        }
        dc.DrawGeometry(Scarf, Pen(Brush("#B3553C"), 1.6), scarfGeometry);

        // The resting pose folds the neck/head forward over the bars.
        // Keep the shoulder anchored; only the upper neck follows the head.
        // Rotating the entire neck lifts its base out of the torso when sleeping.
        var neck = new StreamGeometry();
        using (var c = neck.Open())
        {
            Point Head(double x, double y) => headRotation.Transform(new Point(x, y));
            c.BeginFigure(new Point(154, 137), true, false);
            if (headTilt != 0)
            {
                // Bend below the collar so it remains wrapped around the neck.
                c.BezierTo(new(178, 129), Head(180, 120), Head(180, 110), true, true);
                c.BezierTo(Head(183, 100), Head(183, 88), Head(182, 76), true, true);
            }
            else c.BezierTo(new(178, 129), Head(185, 111), Head(182, 76), true, true);
            c.BezierTo(Head(178, 46), Head(183, 25), Head(202, 23), true, true);
            c.BezierTo(Head(223, 16), Head(240, 31), Head(234, 56), true, true);
            if (headTilt != 0)
            {
                c.BezierTo(Head(228, 78), Head(219, 99), Head(220, 113), true, true);
                c.BezierTo(Head(220, 123), new(222, 129), new(223, 136), true, true);
            }
            else c.BezierTo(Head(228, 78), Head(219, 111), new(223, 136), true, true);
            c.QuadraticBezierTo(new(213, 154), new(195, 159), true, true);
        }
        dc.DrawGeometry(Cream, Outline, neck);
        dc.PushTransform(headRotation);
        Shape(dc, "M 181,99 Q 199,108 221,101 L 220,113 Q 197,119 180,110 Z", Scarf, Pen(Brush("#B3553C"), 1.5));
        Shape(dc, "M 192,27 Q 188,17 179,15 Q 191,14 200,25 M 203,23 Q 198,10 201,5 Q 212,14 213,24", Cream, Outline);
        Shape(dc, "M 226,58 Q 227,103 255,107 Q 287,111 323,71 Z", Orange);
        Shape(dc, "M 233,64 Q 237,90 259,95 Q 281,99 308,79", null, Pen(Brush("#FFDA79"), 2.4));
        Shape(dc, "M 224,52 Q 223,45 231,47 L 332,67 Q 340,70 330,75 Q 269,72 227,63 Q 222,60 224,52 Z", Gold);
        Shape(dc, "M 235,52 L 311,67", null, Pen(Brush("#FFE396"), 2.2));
        var blinking = Action == PetAction.Sleep || Math.Sin(Phase * .21) > .994;
        if (blinking) Shape(dc, "M 208,45 Q 214,50 220,44", null, Outline);
        else
        {
            dc.DrawEllipse(Ink, null, new Point(215, 43), 3.8, 5);
            dc.DrawEllipse(Brush("#FFFFFF"), null, new Point(216, 41), 1.1, 1.3);
        }
        dc.DrawEllipse(Brush("#F3BB93"), null, new Point(211, 60), 5, 3);
        dc.Pop();
        dc.Pop(); // Keep the hand on the handlebar while the torso leans.

        Shape(dc, "M 108,132 C 128,115 147,130 165,144 C 182,157 219,144 260,141 Q 274,140 275,148 Q 274,155 260,157 C 218,164 181,179 150,165 Q 127,158 117,146", Cream);
        Shape(dc, "M 117,136 Q 137,133 150,144 M 127,145 Q 141,148 150,154", null, Pen(Brush("#A6B5A0"), 1.7));
        dc.Pop();
        if (Action == PetAction.Sleep)
        {
            var text = new FormattedText("z  z", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 15, Brush("#6E877D"), VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(260, 34 + Math.Sin(Phase) * 3));
        }
        dc.Pop(); dc.Pop();
        if (Direction < 0) dc.Pop();
        dc.Pop(); dc.Pop();
    }

    private void Wheel(DrawingContext dc, Point center, double phase)
    {
        dc.DrawEllipse(null, Pen(Ink, 6), center, 52, 52);
        dc.DrawEllipse(null, Pen(Cream, 5), center, 47.5, 47.5);
        dc.DrawEllipse(null, Pen(Brush("#B9C5AC"), 1), center, 45, 45);
        for (var i = 0; i < 16; i++)
        {
            var angle = i * Math.PI / 8 + phase;
            Line(dc, center, new Point(center.X + Math.Cos(angle) * 45, center.Y + Math.Sin(angle) * 45), Brush("#628779"), 1);
        }
        var reflector = new Point(center.X + Math.Cos(phase + 1) * 37, center.Y + Math.Sin(phase + 1) * 37);
        dc.DrawEllipse(Gold, null, reflector, 2.6, 3.6);
        dc.DrawEllipse(Green, Outline, center, 6, 6);
        // Open wheel centers remain transparent; the cream is only a narrow rim.
    }

    private void Leg(DrawingContext dc, Point hip, Point foot, Brush color)
    {
        var dx = foot.X - hip.X; var dy = foot.Y - hip.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var offset = Math.Sqrt(Math.Max(0, 58 * 58 - length * length / 4));
        var knee = new Point((hip.X + foot.X) / 2 + dy / Math.Max(1, length) * offset,
            (hip.Y + foot.Y) / 2 - dx / Math.Max(1, length) * offset);
        Line(dc, hip, knee, Brush("#C68C33"), 9);
        Line(dc, knee, foot, Brush("#C68C33"), 9);
        Line(dc, hip, knee, color, 6);
        Line(dc, knee, foot, color, 6);
        dc.DrawRoundedRectangle(color, Pen(Brush("#B98634"), 1.6), new Rect(foot.X - 5, foot.Y - 3, 20, 7), 3, 3);
    }

    private void Shape(DrawingContext dc, string path, Brush? fill) => Shape(dc, path, fill, Outline);
    private void Shape(DrawingContext dc, string path, Brush? fill, Pen? pen)
    {
        if (!_paths.TryGetValue(path, out var geometry)) { geometry = Geometry.Parse(path); geometry.Freeze(); _paths.Add(path, geometry); }
        dc.DrawGeometry(fill, pen, geometry);
    }
    private static void Line(DrawingContext dc, Point from, Point to, Brush brush, double width) => dc.DrawLine(Pen(brush, width), from, to);
    private static Brush Brush(string value) => Brushes.GetOrAdd(value, key => { var result = (SolidColorBrush)new BrushConverter().ConvertFromString(key)!; result.Freeze(); return result; });
    private static Pen Pen(Brush brush, double width) => Pens.GetOrAdd((brush, width), key => { var p = new Pen(key.Item1, key.Item2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; });
}
