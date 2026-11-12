using System;
using System.Windows;
using System.Windows.Media;
using Nexus.Engine.ImageSubsystem.Ocr;

namespace Nexus.App.ViewModels;

/// <summary>
/// Presentation ViewModel for a single word recognized by OneOCR.
/// Uses native 4-point quadrilateral geometry directly from OneOCR.
/// </summary>
public sealed class OneOcrWordViewModel : ViewModelBase
{
    public int GlobalIndex { get; init; }
    public int LineIndex { get; init; }
    public int WordIndex { get; init; }
    public string Text { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public OcrQuad Quad { get; init; }

    public double X1 => Quad.MinX;
    public double Y1 => Quad.MinY;
    public double Width => Quad.Width;
    public double Height => Quad.Height;

    public Geometry PolygonGeometry { get; init; } = Geometry.Empty;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(IsHoverOnly));
            }
        }
    }

    private bool _isHovered;
    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetProperty(ref _isHovered, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(IsHoverOnly));
            }
        }
    }

    public bool IsHoverOnly => IsHovered && !IsSelected;

    public Brush BackgroundBrush => IsSelected
        ? _selectedBackgroundBrush
        : (IsHovered ? _hoverBackgroundBrush : _transparentBrush);

    public Brush BorderBrush => IsSelected
        ? _selectedBorderBrush
        : (IsHovered ? _hoverBorderBrush : _transparentBrush);

    public Brush HoverBackgroundBrush => _hoverBackgroundBrush;
    public Brush HoverBorderBrush => _hoverBorderBrush;

    public string TooltipText => $"\"{Text}\" (Confidence: {Confidence:P0})";

    private static readonly Brush _selectedBackgroundBrush = new SolidColorBrush(Color.FromArgb(120, 0, 120, 212));
    private static readonly Brush _hoverBackgroundBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
    private static readonly Brush _transparentBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    private static readonly Brush _selectedBorderBrush = new SolidColorBrush(Color.FromArgb(220, 0, 120, 212));
    private static readonly Brush _hoverBorderBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));

    static OneOcrWordViewModel()
    {
        _selectedBackgroundBrush.Freeze();
        _hoverBackgroundBrush.Freeze();
        _transparentBrush.Freeze();
        _selectedBorderBrush.Freeze();
        _hoverBorderBrush.Freeze();
    }

    public static Geometry CreateQuadGeometry(OcrQuad quad)
    {
        var streamGeom = new StreamGeometry();
        using (var ctx = streamGeom.Open())
        {
            ctx.BeginFigure(new Point(quad.X1, quad.Y1), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(quad.X2, quad.Y2), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(quad.X3, quad.Y3), isStroked: true, isSmoothJoin: false);
            ctx.LineTo(new Point(quad.X4, quad.Y4), isStroked: true, isSmoothJoin: false);
        }
        if (streamGeom.CanFreeze) streamGeom.Freeze();
        return streamGeom;
    }

    /// <summary>
    /// Constructs a single unified PathFigure spanning all contiguous words in a line run,
    /// seamlessly covering the words and the inter-word spaces without interior divider lines.
    /// </summary>
    public static PathFigure CreateContiguousRunFigure(IReadOnlyList<OneOcrWordViewModel> run)
    {
        if (run.Count == 0) return new PathFigure();

        var figure = new PathFigure
        {
            StartPoint = new Point(run[0].Quad.X1, run[0].Quad.Y1),
            IsFilled = true,
            IsClosed = true
        };

        // Top edge: from start of first word across to end of last word
        for (int i = 0; i < run.Count; i++)
        {
            if (i > 0)
            {
                figure.Segments.Add(new LineSegment(new Point(run[i].Quad.X1, run[i].Quad.Y1), isStroked: true));
            }
            figure.Segments.Add(new LineSegment(new Point(run[i].Quad.X2, run[i].Quad.Y2), isStroked: true));
        }

        // Right edge of last word
        figure.Segments.Add(new LineSegment(new Point(run[^1].Quad.X3, run[^1].Quad.Y3), isStroked: true));

        // Bottom edge in reverse: from bottom of last word back to first word
        for (int i = run.Count - 1; i >= 0; i--)
        {
            if (i < run.Count - 1)
            {
                figure.Segments.Add(new LineSegment(new Point(run[i].Quad.X3, run[i].Quad.Y3), isStroked: true));
            }
            figure.Segments.Add(new LineSegment(new Point(run[i].Quad.X4, run[i].Quad.Y4), isStroked: true));
        }

        return figure;
    }
}
