using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using VtNetCore.VirtualTerminal;
using VtNetCore.VirtualTerminal.Model;
using VtNetCore.VirtualTerminal.Enums;
using ClaudeOrchestrator.WPF.Services;

namespace ClaudeOrchestrator.WPF.Views;

public class TerminalView : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private readonly VisualCollection _visuals;
    private PtySessionService? _session;

    private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, Courier New");
    private static readonly Typeface MonoTypeface = new(MonoFont,
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private const double FontSize = 13.0;

    private double _charWidth = 8.0;
    private double _charHeight = 16.0;

    // ANSI 8-color palette (standard terminal colors)
    private static readonly Color[] AnsiColors =
    [
        Color.FromRgb(0,   0,   0),    // Black
        Color.FromRgb(170, 0,   0),    // Red
        Color.FromRgb(0,   170, 0),    // Green
        Color.FromRgb(170, 170, 0),    // Yellow
        Color.FromRgb(0,   0,   170),  // Blue
        Color.FromRgb(170, 0,   170),  // Magenta
        Color.FromRgb(0,   170, 170),  // Cyan
        Color.FromRgb(170, 170, 170),  // White
    ];

    public TerminalView()
    {
        _visuals = new VisualCollection(this);
        _visuals.Add(_visual);
        Focusable = true;
        ClipToBounds = true;
        // Background is painted in Render() via DrawRectangle — FrameworkElement has no Background property
        MeasureCharSize();
        SizeChanged += OnSizeChanged;
        MouseDown += (_, _) => Focus();
    }

    public void Attach(PtySessionService session)
    {
        if (_session != null)
            _session.ViewportInvalidated -= OnViewportInvalidated;
        _session = session;
        _session.ViewportInvalidated += OnViewportInvalidated;
        Render();
    }

    public void Detach()
    {
        if (_session != null)
            _session.ViewportInvalidated -= OnViewportInvalidated;
        _session = null;
        ClearVisual();
    }

    private void OnViewportInvalidated() => Dispatcher.InvokeAsync(Render);

    private void Render()
    {
        var terminal = _session?.Terminal;
        if (terminal is null) { ClearVisual(); return; }

        using var dc = _visual.RenderOpen();
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));

        var vp = terminal.ViewPort;
        if (vp is null) return;

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int rowCount = terminal.VisibleRows;
        int colCount = terminal.VisibleColumns;

        for (int row = 0; row < rowCount; row++)
        {
            TerminalLine? line;
            try { line = vp.GetVisibleLine(row); }
            catch { continue; }

            if (line is null) continue;

            for (int col = 0; col < colCount; col++)
            {
                try
                {
                    // TerminalLine has an indexer: TerminalCharacter this[int index]
                    var cell = col < line.Count ? line[col] : null;

                    string ch = " ";
                    TerminalAttribute? attrs = null;

                    if (cell != null)
                    {
                        ch = cell.Char == '\0' ? " " : cell.Char.ToString();
                        attrs = cell.Attributes;
                    }

                    var rect = new Rect(col * _charWidth, row * _charHeight, _charWidth, _charHeight);

                    // Background
                    var bg = GetBackground(attrs);
                    if (bg != null)
                        dc.DrawRectangle(bg, null, rect);

                    // Glyph
                    if (ch != " " && ch != "\0")
                    {
                        var fg = GetForeground(attrs);
                        var ft = new FormattedText(ch, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, MonoTypeface, FontSize, fg, dpi);
                        dc.DrawText(ft, rect.Location);
                    }
                }
                catch { /* skip bad cells */ }
            }
        }

        // Cursor
        try
        {
            var cur = vp.CursorPosition;
            if (cur != null && cur.IsValid)
            {
                var cursorRect = new Rect(
                    cur.Column * _charWidth,
                    cur.Row * _charHeight,
                    _charWidth, _charHeight);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 200, 200, 200)), null, cursorRect);
            }
        }
        catch { }
    }

    private static Brush GetBackground(TerminalAttribute? attrs)
    {
        if (attrs == null) return Brushes.Black;

        // Check for RGB color first
        if (attrs.BackgroundRgb is { } bgRgb)
        {
            var c = Color.FromRgb(
                (byte)Math.Clamp(bgRgb.Red, 0, 255),
                (byte)Math.Clamp(bgRgb.Green, 0, 255),
                (byte)Math.Clamp(bgRgb.Blue, 0, 255));
            // Avoid painting black-on-black for default background
            if (c == Colors.Black) return Brushes.Black;
            return new SolidColorBrush(c);
        }

        // Fall back to ANSI palette
        var idx = (int)attrs.BackgroundColor;
        if (idx > 0 && idx < AnsiColors.Length)
            return new SolidColorBrush(AnsiColors[idx]);

        return Brushes.Black;
    }

    private static Brush GetForeground(TerminalAttribute? attrs)
    {
        if (attrs == null) return Brushes.LightGray;

        // Check for RGB color first
        if (attrs.ForegroundRgb is { } fgRgb)
        {
            return new SolidColorBrush(Color.FromRgb(
                (byte)Math.Clamp(fgRgb.Red, 0, 255),
                (byte)Math.Clamp(fgRgb.Green, 0, 255),
                (byte)Math.Clamp(fgRgb.Blue, 0, 255)));
        }

        // Fall back to ANSI palette
        var idx = (int)attrs.ForegroundColor;
        if (idx >= 0 && idx < AnsiColors.Length)
        {
            var c = AnsiColors[idx];
            // Bright modifier
            if (attrs.Bright && idx < AnsiColors.Length)
                c = Brighten(c);
            return new SolidColorBrush(c);
        }

        return Brushes.LightGray;
    }

    private static Color Brighten(Color c) =>
        Color.FromRgb(
            (byte)Math.Min(255, c.R + 85),
            (byte)Math.Min(255, c.G + 85),
            (byte)Math.Min(255, c.B + 85));

    private void ClearVisual()
    {
        using var dc = _visual.RenderOpen();
        dc.DrawRectangle(Brushes.Black, null, new Rect(RenderSize));
    }

    // Keyboard input
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_session is null) return;
        // Ctrl+V — paste text
        if (e.Key == Key.V && (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0)
        {
            e.Handled = true;
            var text = Clipboard.GetText();
            if (!string.IsNullOrEmpty(text))
                _ = _session.WriteAsync(text);
            return;
        }
        // Special keys
        var bytes = KeyToAnsi(e);
        if (bytes != null)
        {
            _ = _session.WriteAsync(bytes);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_session is null || string.IsNullOrEmpty(e.Text)) return;
        _ = _session.WriteAsync(e.Text);
        e.Handled = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_session is null) return;
        var cols = Math.Max(1, (int)(e.NewSize.Width / _charWidth));
        var rows = Math.Max(1, (int)(e.NewSize.Height / _charHeight));
        _ = _session.ResizeAsync(cols, rows);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // TODO: scrollback buffer — future enhancement
        base.OnMouseWheel(e);
    }

    private void MeasureCharSize()
    {
        var ft = new FormattedText("W", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, MonoTypeface, FontSize, Brushes.White, 96.0);
        _charWidth = ft.Width;
        _charHeight = ft.Height + 2; // small padding
    }

    private static byte[]? KeyToAnsi(KeyEventArgs e)
    {
        var ctrl = (e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0;
        return e.Key switch
        {
            Key.Return     => "\r"u8.ToArray(),
            Key.Back       => "\x7f"u8.ToArray(),
            Key.Tab        => "\t"u8.ToArray(),
            Key.Escape     => "\x1b"u8.ToArray(),
            Key.Up         => "\x1b[A"u8.ToArray(),
            Key.Down       => "\x1b[B"u8.ToArray(),
            Key.Right      => "\x1b[C"u8.ToArray(),
            Key.Left       => "\x1b[D"u8.ToArray(),
            Key.Home       => "\x1b[H"u8.ToArray(),
            Key.End        => "\x1b[F"u8.ToArray(),
            Key.Delete     => "\x1b[3~"u8.ToArray(),
            Key.PageUp     => "\x1b[5~"u8.ToArray(),
            Key.PageDown   => "\x1b[6~"u8.ToArray(),
            Key.F1         => "\x1bOP"u8.ToArray(),
            Key.F2         => "\x1bOQ"u8.ToArray(),
            Key.F3         => "\x1bOR"u8.ToArray(),
            Key.F4         => "\x1bOS"u8.ToArray(),
            Key.C when ctrl => "\x03"u8.ToArray(),
            Key.D when ctrl => "\x04"u8.ToArray(),
            Key.L when ctrl => "\x0c"u8.ToArray(),
            Key.Z when ctrl => "\x1a"u8.ToArray(),
            _ => null
        };
    }

    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
}
