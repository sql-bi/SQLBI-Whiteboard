using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// Writes one picture per slide as a .pptx that PowerPoint opens without repairing.
/// Every part is built here rather than copied from a template, so the package carries
/// only what the deck needs: a master, a layout, a theme, a notes master, and the slides.
/// </summary>
public static class PptxDeckWriter
{
    // Slide geometry in EMU (914400 per inch). Both aspects share the 7.5 in height.
    private const long SlideHeight = 6858000;
    private const long WideSlideWidth = 12192000;
    private const long StandardSlideWidth = 9144000;
    private const long NotesPageWidth = 6858000;
    private const long NotesPageHeight = 9144000;
    private const long Margin = 274320;
    private const long TitleTop = 228600;
    private const long TitleHeight = 548640;
    private const long PictureTop = 914400;
    private const long EmuPerPoint = 12700;
    private const int TextBoxBorderWidth = 9525;
    private const double TextBoxCornerRadius = 4;

    // A stadium is the rounded rectangle whose corners have taken the whole of
    // its shorter side, which is the outline the board draws for it.
    private const double StadiumCornerFraction = 0.5;
    private const double Epsilon = 0.000001;

    private const int TitleFontSize = 2000;
    private const string TitleColor = "1F2937";
    private const string TitleFont = "Segoe UI Semibold";
    private const string ThemeFont = "Segoe UI";
    private const string Language = "en-US";

    // PowerPoint requires master and layout ids from 2^31 up and slide ids from 256 up.
    private const uint MasterId = 2147483648;
    private const uint LayoutId = 2147483649;
    private const uint FirstSlideId = 256;
    private const uint GroupShapeId = 1;
    private const uint TitleShapeId = 2;
    private const uint PictureShapeId = 3;

    public static void Write(Stream destination, IReadOnlyList<ExportPage> pages, DeckOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(pages);

        var slideWidth = (options?.Aspect ?? SlideAspect.Wide) == SlideAspect.Standard
            ? StandardSlideWidth
            : WideSlideWidth;

        using var document = PresentationDocument.Create(destination, PresentationDocumentType.Presentation);
        var presentationPart = document.AddPresentationPart();

        var masterPart = presentationPart.AddNewPart<SlideMasterPart>();
        var themePart = masterPart.AddNewPart<ThemePart>();
        themePart.Theme = BuildTheme();
        presentationPart.AddPart(themePart);

        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
        layoutPart.AddPart(masterPart);
        masterPart.SlideMaster = DeclareNamespaces(BuildSlideMaster(slideWidth, masterPart.GetIdOfPart(layoutPart)));
        layoutPart.SlideLayout = DeclareNamespaces(BuildSlideLayout(slideWidth));

        var notesMasterPart = presentationPart.AddNewPart<NotesMasterPart>();
        notesMasterPart.AddNewPart<ThemePart>().Theme = BuildTheme();
        notesMasterPart.NotesMaster = DeclareNamespaces(BuildNotesMaster());

        var slideIds = new P.SlideIdList();
        for (var index = 0; index < pages.Count; index++)
        {
            var slidePart = AddSlide(presentationPart, layoutPart, notesMasterPart, pages[index], slideWidth);
            slideIds.Append(new P.SlideId
            {
                Id = FirstSlideId + (uint)index,
                RelationshipId = presentationPart.GetIdOfPart(slidePart),
            });
        }

        presentationPart.Presentation = DeclareNamespaces(new P.Presentation(
            new P.SlideMasterIdList(new P.SlideMasterId
            {
                Id = MasterId,
                RelationshipId = presentationPart.GetIdOfPart(masterPart),
            }),
            new P.NotesMasterIdList(new P.NotesMasterId { Id = presentationPart.GetIdOfPart(notesMasterPart) }),
            slideIds,
            new P.SlideSize { Cx = (int)slideWidth, Cy = (int)SlideHeight },
            new P.NotesSize { Cx = NotesPageWidth, Cy = NotesPageHeight },
            new P.DefaultTextStyle(
                new A.DefaultParagraphProperties(new A.DefaultRunProperties { Language = Language }),
                TextLevel<A.Level1ParagraphProperties>(1800))));
    }

    private static SlidePart AddSlide(
        PresentationPart presentationPart,
        SlideLayoutPart layoutPart,
        NotesMasterPart notesMasterPart,
        ExportPage page,
        long slideWidth)
    {
        if (page.PixelWidth <= 0 || page.PixelHeight <= 0)
        {
            throw new ArgumentException($"Page '{page.Title}' has no pixel size.", nameof(page));
        }

        var slidePart = presentationPart.AddNewPart<SlidePart>();
        slidePart.AddPart(layoutPart);

        var fit = PageFit.Of(slideWidth, page.PixelWidth, page.PixelHeight);
        var shapes = new List<OpenXmlElement> { TitleShape(slideWidth, page.Title) };
        if (page.Elements is null)
        {
            var imageId = AddImage(slidePart, page.Png, ImagePartType.Png);
            shapes.Add(PictureShape(PictureShapeId, "Picture", imageId, fit.Frame()));
        }
        else
        {
            // Every element's id is known before any of them is written, because a
            // connector names the shape it is tied to and that shape may be listed
            // after it.
            Dictionary<Guid, (uint Id, string Preset)> shapeIds = ShapeIds(page.Elements);

            // Elements are listed back to front, which is also the shape tree's z-order.
            for (var index = 0; index < page.Elements.Count; index++)
            {
                shapes.Add(ElementShape(
                    slidePart,
                    page.Elements[index],
                    PictureShapeId + (uint)index,
                    fit,
                    shapeIds));
            }
        }

        slidePart.Slide = DeclareNamespaces(new P.Slide(
            new P.CommonSlideData(ShapeTree(shapes.ToArray())),
            new P.ColorMapOverride(new A.MasterColorMapping())));

        if (!string.IsNullOrEmpty(page.Notes))
        {
            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.AddPart(notesMasterPart);
            notesPart.AddPart(slidePart);
            notesPart.NotesSlide = DeclareNamespaces(BuildNotesSlide(page.Notes));
        }

        return slidePart;
    }

    private static OpenXmlElement ElementShape(
        SlidePart slidePart,
        SlideElement element,
        uint id,
        PageFit fit,
        IReadOnlyDictionary<Guid, (uint Id, string Preset)> shapeIds) => element switch
    {
        SlideImageElement image => PictureShape(
            id,
            $"Picture {id}",
            AddImage(slidePart, image.Data, ImageType(image.ContentType)),
            fit.Frame(image.Bounds)),
        SlideTextElement text => TextBoxShape(id, text, fit),
        SlideShapeElement shape => GeometryShape(id, shape, fit),
        SlideLabelElement label => LabelShape(id, label, fit),
        SlideConnectorElement connector => ConnectorShape(id, connector, fit, shapeIds),
        _ => throw new ArgumentException($"Unsupported slide element {element.GetType().Name}.", nameof(element)),
    };

    /// <summary>
    /// The slide id and the preset of every element a connector could be tied
    /// to, by the board object it came from. Only a shape and a label qualify:
    /// both go out as p:sp with a preset geometry that carries connection sites,
    /// while a picture or a text box has nothing an arrow can hold on to.
    /// </summary>
    private static Dictionary<Guid, (uint Id, string Preset)> ShapeIds(IReadOnlyList<SlideElement> elements)
    {
        var ids = new Dictionary<Guid, (uint Id, string Preset)>();
        for (var index = 0; index < elements.Count; index++)
        {
            (Guid objectId, string preset) = elements[index] switch
            {
                SlideShapeElement shape => (shape.ObjectId, PresetName(Preset(shape))),
                SlideLabelElement label => (label.ObjectId, PresetName(Rectangle())),
                _ => (Guid.Empty, string.Empty),
            };
            if (objectId != Guid.Empty)
            {
                ids[objectId] = (PictureShapeId + (uint)index, preset);
            }
        }

        return ids;
    }

    /// <summary>
    /// The preset's own name, read back from the geometry that was built for
    /// the shape rather than worked out a second time, so the sites a connector
    /// names always belong to the geometry the shape actually went out with.
    /// </summary>
    private static string PresetName(A.PresetGeometry geometry) => geometry.Preset?.InnerText ?? string.Empty;

    /// <summary>
    /// What a connector's end holds on to, as PowerPoint wants it: the shape's
    /// id and the number of the site on it. An end bound to nothing, to
    /// something that did not go out as a shape, or to a point the preset has no
    /// site for has no answer, and the arrow then stays where it was drawn.
    /// </summary>
    private static (uint Id, int Site)? ConnectionOn(
        SlideConnection? connection,
        IReadOnlyDictionary<Guid, (uint Id, string Preset)> shapeIds)
    {
        if (connection is not { } bound || !shapeIds.TryGetValue(bound.ObjectId, out (uint Id, string Preset) target))
        {
            return null;
        }

        return PresetConnectionSites.IndexFor(target.Preset, bound.U, bound.V) is { } site
            ? (target.Id, site)
            : null;
    }

    private static string AddImage(SlidePart slidePart, byte[] data, PartTypeInfo type)
    {
        var imagePart = slidePart.AddImagePart(type);
        using (var stream = new MemoryStream(data, writable: false))
        {
            imagePart.FeedData(stream);
        }

        return slidePart.GetIdOfPart(imagePart);
    }

    private static PartTypeInfo ImageType(string contentType) =>
        string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
            ? ImagePartType.Jpeg
            : ImagePartType.Png;

    private static P.Picture PictureShape(uint id, string name, string imageRelationshipId, A.Transform2D frame) => new(
        new P.NonVisualPictureProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true }),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.BlipFill(new A.Blip { Embed = imageRelationshipId }, new A.Stretch(new A.FillRectangle())),
        new P.ShapeProperties(frame, Rectangle()));

    /// <summary>
    /// A text container as a plain text box: the title line in the deck's title face, then
    /// the body runs. Autofit is off so that PowerPoint keeps the sizes the screen used.
    /// </summary>
    private static P.Shape TextBoxShape(uint id, SlideTextElement text, PageFit fit)
    {
        var titleSize = fit.Points(text.TitleFontSize);
        var bodySize = fit.Points(text.BodyFontSize);
        var inset = (int)fit.Emu(text.Padding);

        var body = new P.TextBody(
            new A.BodyProperties(new A.NoAutoFit())
            {
                Wrap = A.TextWrappingValues.Square,
                LeftInset = inset,
                TopInset = inset,
                RightInset = inset,
                BottomInset = inset,
                Anchor = A.TextAnchoringTypeValues.Top,
            },
            new A.ListStyle());

        var title = new A.Paragraph(new A.ParagraphProperties(
            new A.SpaceAfter(new A.SpacingPoints { Val = (int)Math.Round(0.4 * bodySize) })));
        if (text.Title.Length > 0)
        {
            title.Append(new A.Run(RunProperties(text.TextArgb, TitleFont, titleSize), new A.Text(text.Title)));
        }

        title.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = titleSize });
        body.Append(title);

        if (text.Paragraphs is { } paragraphs)
        {
            foreach (var item in paragraphs)
            {
                var properties = new A.ParagraphProperties
                {
                    LeftMargin = (int)fit.Emu(item.LeftMargin),
                    Indent = -(int)fit.Emu(item.HangingIndent),
                };
                if (item.Bullet is { } bullet)
                {
                    properties.Append(new A.BulletFont { Typeface = text.FontFamily });
                    properties.Append(new A.CharacterBullet { Char = bullet });
                }
                else
                {
                    properties.Append(new A.NoBullet());
                }

                var itemParagraph = new A.Paragraph(properties);
                foreach (var run in item.Runs)
                {
                    itemParagraph.Append(new A.Run(
                        RunProperties(run.Argb, text.FontFamily, bodySize, run.Bold, run.Italic),
                        new A.Text(run.Text)));
                }

                itemParagraph.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = bodySize });
                body.Append(itemParagraph);
            }
        }

        var paragraph = new A.Paragraph();
        foreach (var run in text.Paragraphs is null ? text.Runs : [])
        {
            var lines = run.Text.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (index > 0)
                {
                    paragraph.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = bodySize });
                    body.Append(paragraph);
                    paragraph = new A.Paragraph();
                }

                var fragment = lines[index].TrimEnd('\r');
                if (fragment.Length > 0)
                {
                    paragraph.Append(new A.Run(
                        RunProperties(run.Argb, text.FontFamily, bodySize, run.Bold, run.Italic),
                        new A.Text(fragment)));
                }
            }
        }

        if (text.Paragraphs is null && text.Runs.Count > 0)
        {
            paragraph.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = bodySize });
            body.Append(paragraph);
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Text {id}" },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                fit.Frame(text.Bounds),
                RoundedRectangle(text.Bounds),
                new A.SolidFill(Rgb(text.BackgroundArgb)),
                new A.Outline(new A.SolidFill(Rgb(text.BorderArgb))) { Width = TextBoxBorderWidth }),
            body);
    }

    /// <summary>
    /// A shape as a shape: the preset whose outline is the one the board draws, a
    /// tinted fill that keeps its translucency, and the outline as a line of the
    /// width the screen gives it, turned about its centre by a:xfrm as a label is.
    /// The text body is what lets PowerPoint type into the shape once the deck is
    /// open, and it carries whatever the shape already says.
    /// </summary>
    private static P.Shape GeometryShape(uint id, SlideShapeElement shape, PageFit fit) => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = $"Shape {id}" },
            new P.NonVisualShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.ShapeProperties(
            TurnedFrame(fit, shape.Bounds, shape.AngleDegrees),
            Preset(shape),
            shape.FillArgb is { } fill ? new A.SolidFill(Translucent(fill)) : (OpenXmlElement)new A.NoFill(),
            new A.Outline(new A.SolidFill(Rgb(shape.OutlineArgb)), new A.Round())
            {
                Width = Math.Max(1, (int)fit.Emu(shape.Thickness)),
                CapType = A.LineCapValues.Round,
            }),
        ShapeTextBody(shape, fit));

    /// <summary>
    /// What a shape says, in the shape: centred across the box and down it,
    /// wrapped, and inset so that the rectangle left for the words is the one
    /// the board writes them in - which for a triangle or a diamond is a good
    /// deal smaller than the box. A shape that says nothing writes the one empty
    /// paragraph a p:sp has to have.
    /// </summary>
    private static P.TextBody ShapeTextBody(SlideShapeElement shape, PageFit fit)
    {
        RectD box = ShapeGeometry.TextBox(
            shape.Kind,
            new RectD(shape.Bounds.X, shape.Bounds.Y, shape.Bounds.Width, shape.Bounds.Height),
            shape.TextMargin);
        var size = fit.Points(shape.FontSize);
        var body = new P.TextBody(
            new A.BodyProperties(new A.NoAutoFit())
            {
                Wrap = A.TextWrappingValues.Square,
                LeftInset = (int)fit.Emu(Math.Max(0, box.Left - shape.Bounds.X)),
                TopInset = (int)fit.Emu(Math.Max(0, box.Top - shape.Bounds.Y)),
                RightInset = (int)fit.Emu(Math.Max(0, shape.Bounds.X + shape.Bounds.Width - box.Right)),
                BottomInset = (int)fit.Emu(Math.Max(0, shape.Bounds.Y + shape.Bounds.Height - box.Bottom)),
                Anchor = A.TextAnchoringTypeValues.Center,
                AnchorCenter = false,
            },
            new A.ListStyle());

        foreach (var line in shape.Text.Split('\n'))
        {
            var paragraph = new A.Paragraph(
                new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Center });
            var text = line.TrimEnd('\r');
            if (text.Length > 0)
            {
                paragraph.Append(new A.Run(
                    RunProperties(shape.TextArgb, shape.FontFamily, size, shape.Bold, shape.Italic, shape.Underline),
                    new A.Text(text)));
            }

            paragraph.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = size });
            body.Append(paragraph);
        }

        return body;
    }

    /// <summary>
    /// A label as a text box with no fill and no line, turned about its centre by
    /// a:xfrm. Wrapping and autofit are off and the box is the size the board
    /// measured the text at, so PowerPoint keeps the lines the screen shows rather
    /// than reflowing them at its own idea of the width.
    /// </summary>
    private static P.Shape LabelShape(uint id, SlideLabelElement label, PageFit fit)
    {
        var size = fit.Points(label.FontSize);
        var body = new P.TextBody(
            new A.BodyProperties(new A.NoAutoFit())
            {
                Wrap = A.TextWrappingValues.None,
                LeftInset = 0,
                TopInset = 0,
                RightInset = 0,
                BottomInset = 0,
                Anchor = A.TextAnchoringTypeValues.Top,
                AnchorCenter = false,
            },
            new A.ListStyle());

        foreach (var line in label.Text.Split('\n'))
        {
            var paragraph = new A.Paragraph(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left });
            var text = line.TrimEnd('\r');
            if (text.Length > 0)
            {
                paragraph.Append(new A.Run(
                    RunProperties(label.Argb, label.FontFamily, size, label.Bold, label.Italic, label.Underline),
                    new A.Text(text)));
            }

            paragraph.Append(new A.EndParagraphRunProperties { Language = Language, FontSize = size });
            body.Append(paragraph);
        }

        A.Transform2D frame = TurnedFrame(fit, label.Bounds, label.AngleDegrees);
        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Label {id}" },
                new P.NonVisualShapeDrawingProperties { TextBox = true },
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(frame, Rectangle(), new A.NoFill(), new A.Outline(new A.NoFill())),
            body);
    }

    /// <summary>
    /// A straight connector is a connector, turned by the flips PowerPoint expects
    /// when it runs right to left or bottom to top. A curved one is a freeform
    /// shape holding the cubic the board drew, because no preset bends the way that
    /// cubic does and a connector with a geometry of its own is not something
    /// PowerPoint writes itself. Either way the head is PowerPoint's own triangle
    /// tail end rather than a filled shape of ours, so it stays an arrow when the
    /// line is recoloured or reshaped.
    /// </summary>
    private static OpenXmlElement ConnectorShape(
        uint id,
        SlideConnectorElement connector,
        PageFit fit,
        IReadOnlyDictionary<Guid, (uint Id, string Preset)> shapeIds)
    {
        var line = new A.Outline(new A.SolidFill(Rgb(connector.Argb)), new A.Round())
        {
            Width = Math.Max(1, (int)fit.Emu(connector.Thickness)),
            CapType = A.LineCapValues.Round,
        };
        if (connector.Kind != ConnectorKind.Line)
        {
            line.Append(new A.TailEnd
            {
                Type = A.LineEndValues.Triangle,
                Width = A.LineEndWidthValues.Medium,
                Length = A.LineEndLengthValues.Medium,
            });
        }

        var frame = fit.Frame(connector.Bounds);
        if (connector.FirstControl is { } first && connector.SecondControl is { } second)
        {
            return new P.Shape(
                new P.NonVisualShapeProperties(
                    new P.NonVisualDrawingProperties { Id = id, Name = $"Connector {id}" },
                    new P.NonVisualShapeDrawingProperties(),
                    new P.ApplicationNonVisualDrawingProperties()),
                new P.ShapeProperties(frame, CurvePath(connector, first, second, fit), new A.NoFill(), line),
                new P.TextBody(
                    new A.BodyProperties(),
                    new A.ListStyle(),
                    new A.Paragraph(new A.EndParagraphRunProperties { Language = Language })));
        }

        if (connector.End.X < connector.Start.X)
        {
            frame.HorizontalFlip = true;
        }

        if (connector.End.Y < connector.Start.Y)
        {
            frame.VerticalFlip = true;
        }

        // The two ends hold on to the shapes they were drawn on, so dragging one
        // of those in PowerPoint drags the arrow with it. An end with no site to
        // name says nothing and stays where it is.
        var connections = new P.NonVisualConnectorShapeDrawingProperties();
        if (ConnectionOn(connector.StartConnection, shapeIds) is { } start)
        {
            connections.Append(new A.StartConnection { Id = start.Id, Index = (uint)start.Site });
        }

        if (ConnectionOn(connector.EndConnection, shapeIds) is { } finish)
        {
            connections.Append(new A.EndConnection { Id = finish.Id, Index = (uint)finish.Site });
        }

        return new P.ConnectionShape(
            new P.NonVisualConnectionShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = $"Connector {id}" },
                connections,
                new P.ApplicationNonVisualDrawingProperties()),
            new P.ShapeProperties(
                frame,
                Geometry(A.ShapeTypeValues.StraightConnector1),
                new A.NoFill(),
                line));
    }

    /// <summary>
    /// The cubic as a freeform path in the shape's own space, which runs from the
    /// box's top-left corner rather than from the slide's. The path is given the
    /// extents as its own width and height, so one EMU on the path is one EMU on
    /// the slide and the curve needs no flips.
    /// </summary>
    private static A.CustomGeometry CurvePath(
        SlideConnectorElement connector,
        SlidePosition first,
        SlidePosition second,
        PageFit fit)
    {
        // The path's own extents are what the shape's extents are scaled against, so
        // they never reach zero: a curve between two side midpoints that face each
        // other has all four of its points on one line, and the box around them has
        // no height at all.
        var width = Math.Max(1, fit.Emu(connector.Bounds.Width));
        var height = Math.Max(1, fit.Emu(connector.Bounds.Height));
        A.Point Local(SlidePosition point) => new()
        {
            X = fit.Emu(point.X - connector.Bounds.X).ToString(CultureInfo.InvariantCulture),
            Y = fit.Emu(point.Y - connector.Bounds.Y).ToString(CultureInfo.InvariantCulture),
        };

        var path = new A.Path { Width = width, Height = height };
        path.Append(new A.MoveTo(Local(connector.Start)));
        path.Append(new A.CubicBezierCurveTo(Local(first), Local(second), Local(connector.End)));
        return new A.CustomGeometry(
            new A.AdjustValueList(),
            new A.ShapeGuideList(),
            new A.AdjustHandleList(),
            new A.ConnectionSiteList(),
            new A.Rectangle { Left = "l", Top = "t", Right = "r", Bottom = "b" },
            new A.PathList(path));
    }

    /// <summary>
    /// The preset whose outline is the one <see cref="ShapeGeometry"/> describes,
    /// with the adjust values that put the corner, the slant, and the arrow's head
    /// where the board puts them. A preset measures its adjusts against its shorter
    /// side, so each fraction of the box is worked back through that side, and a
    /// value past what a preset accepts is pinned by PowerPoint - which is what
    /// makes a stadium out of a rounded rectangle.
    /// </summary>
    private static A.PresetGeometry Preset(SlideShapeElement shape)
    {
        var side = Math.Max(Epsilon, Math.Min(shape.Bounds.Width, shape.Bounds.Height));
        return shape.Kind switch
        {
            ShapeKind.Ellipse => Geometry(A.ShapeTypeValues.Ellipse),
            ShapeKind.Triangle => Geometry(A.ShapeTypeValues.Triangle),
            ShapeKind.Pentagon => Geometry(A.ShapeTypeValues.Pentagon),
            ShapeKind.Diamond => Geometry(A.ShapeTypeValues.Diamond),
            // The arrow is the one preset that measures a thickness against the
            // height rather than against the shorter side, so its shaft is the
            // fraction as it stands and only its head goes through the side.
            ShapeKind.BlockArrow => Geometry(
                A.ShapeTypeValues.RightArrow,
                ("adj1", Adjust(ShapeGeometry.ArrowShaftFraction)),
                ("adj2", Adjust((1 - ShapeGeometry.ArrowHeadFraction) * shape.Bounds.Width / side))),
            ShapeKind.Parallelogram => Geometry(
                A.ShapeTypeValues.Parallelogram,
                ("adj", Adjust(ShapeGeometry.SlantFraction * shape.Bounds.Width / side))),
            ShapeKind.Stadium => Geometry(A.ShapeTypeValues.RoundRectangle, ("adj", Adjust(StadiumCornerFraction))),
            _ => Geometry(A.ShapeTypeValues.RoundRectangle, ("adj", Adjust(ShapeGeometry.CornerFraction))),
        };
    }

    private static A.PresetGeometry Geometry(A.ShapeTypeValues preset, params (string Name, int Value)[] adjusts)
    {
        var values = new A.AdjustValueList();
        foreach (var (name, value) in adjusts)
        {
            values.Append(new A.ShapeGuide { Name = name, Formula = $"val {value}" });
        }

        return new A.PresetGeometry(values) { Preset = preset };
    }

    // An adjust is in hundred-thousandths of the shorter side.
    private static int Adjust(double fraction) => Math.Max(0, (int)Math.Round(fraction * 100000));

    /// <summary>
    /// The rectangle a shape sits in, turned about its own centre - which is
    /// what a:xfrm rot does, and where the board turns it. An upright object
    /// says nothing, so a deck written for one is the deck it always was.
    /// </summary>
    private static A.Transform2D TurnedFrame(PageFit fit, SlideRect bounds, double angleDegrees)
    {
        A.Transform2D frame = fit.Frame(bounds);
        if (angleDegrees != 0)
        {
            frame.Rotation = Rotation(angleDegrees);
        }

        return frame;
    }

    // Sixtieth-thousandths of a degree, clockwise, which is the way the board
    // turns a label too.
    private static int Rotation(double angleDegrees) =>
        (int)Math.Round((((angleDegrees % 360) + 360) % 360) * 60000);

    private static A.RunProperties RunProperties(
        uint argb,
        string typeface,
        int size,
        bool bold = false,
        bool italic = false,
        bool underline = false)
    {
        var properties = new A.RunProperties(new A.SolidFill(Rgb(argb)), new A.LatinFont { Typeface = typeface })
        {
            Language = Language,
            FontSize = size,
        };
        if (bold)
        {
            properties.Bold = true;
        }

        if (italic)
        {
            properties.Italic = true;
        }

        if (underline)
        {
            properties.Underline = A.TextUnderlineValues.Single;
        }

        return properties;
    }

    // The adjust value is the corner radius as a fraction of the shorter side, in
    // 1/100000, so it is derived from the bounds to keep the radius the screen shows.
    private static A.PresetGeometry RoundedRectangle(SlideRect bounds)
    {
        var side = Math.Min(bounds.Width, bounds.Height);
        var adjust = side > 0 ? Math.Clamp((int)Math.Round(TextBoxCornerRadius / side * 100000), 0, 50000) : 0;
        return new A.PresetGeometry(new A.AdjustValueList(new A.ShapeGuide { Name = "adj", Formula = $"val {adjust}" }))
        {
            Preset = A.ShapeTypeValues.RoundRectangle,
        };
    }

    /// <summary>
    /// How a page's pixels land on the slide: scaled uniformly to fit the picture box and
    /// centred in it. The single picture and every element go through the same mapping.
    /// </summary>
    private readonly record struct PageFit(double Scale, long OffsetX, long OffsetY, long Width, long Height)
    {
        public static PageFit Of(long slideWidth, int pixelWidth, int pixelHeight)
        {
            var boxWidth = slideWidth - 2 * Margin;
            var boxHeight = SlideHeight - PictureTop - Margin;
            var scale = Math.Min((double)boxWidth / pixelWidth, (double)boxHeight / pixelHeight);
            var width = (long)Math.Round(pixelWidth * scale);
            var height = (long)Math.Round(pixelHeight * scale);
            return new PageFit(scale, Margin + (boxWidth - width) / 2, PictureTop + (boxHeight - height) / 2, width, height);
        }

        public A.Transform2D Frame() => Transform(OffsetX, OffsetY, Width, Height);

        // Edges are rounded rather than sizes, so that a rectangle inside the page stays
        // inside the picture box after rounding.
        public A.Transform2D Frame(SlideRect rect)
        {
            var left = OffsetX + Emu(rect.X);
            var top = OffsetY + Emu(rect.Y);
            return Transform(left, top, OffsetX + Emu(rect.X + rect.Width) - left, OffsetY + Emu(rect.Y + rect.Height) - top);
        }

        public long Emu(double pixels) => (long)Math.Round(pixels * Scale);

        // Hundredths of a point, the unit of sz and spcPts; never below 1 pt.
        public int Points(double pixels) => Math.Max(100, (int)Math.Round(pixels * Scale / EmuPerPoint * 100));
    }

    /// <summary>
    /// The title placeholder, shared by master, layout, and slide so that PowerPoint
    /// inherits the box through the chain. The slide states the run formatting explicitly
    /// so that the text survives a layout change in PowerPoint.
    /// </summary>
    private static P.Shape TitleShape(long slideWidth, string? text)
    {
        var paragraph = new A.Paragraph(new A.ParagraphProperties { Alignment = A.TextAlignmentTypeValues.Left });
        if (text is not null)
        {
            paragraph.Append(new A.Run(
                new A.RunProperties(new A.SolidFill(Rgb(TitleColor)), new A.LatinFont { Typeface = TitleFont })
                {
                    Language = Language,
                    FontSize = TitleFontSize,
                },
                new A.Text(text)));
        }

        paragraph.Append(new A.EndParagraphRunProperties { Language = Language });

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = TitleShapeId, Name = "Title" },
                new P.NonVisualShapeDrawingProperties(new A.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Title })),
            new P.ShapeProperties(Transform(Margin, TitleTop, slideWidth - 2 * Margin, TitleHeight), Rectangle()),
            new P.TextBody(
                new A.BodyProperties(new A.NoAutoFit())
                {
                    Wrap = A.TextWrappingValues.None,
                    LeftInset = 0,
                    RightInset = 0,
                    Anchor = A.TextAnchoringTypeValues.Center,
                },
                new A.ListStyle(),
                paragraph));
    }

    private static P.SlideMaster BuildSlideMaster(long slideWidth, string layoutRelationshipId) => new(
        new P.CommonSlideData(WhiteBackground(), ShapeTree(TitleShape(slideWidth, text: null))),
        ColorMap(),
        new P.SlideLayoutIdList(new P.SlideLayoutId { Id = LayoutId, RelationshipId = layoutRelationshipId }),
        new P.TextStyles(
            new P.TitleStyle(new A.Level1ParagraphProperties(
                new A.DefaultRunProperties(new A.SolidFill(Rgb(TitleColor)), new A.LatinFont { Typeface = TitleFont })
                {
                    FontSize = TitleFontSize,
                })
            {
                Alignment = A.TextAlignmentTypeValues.Left,
            }),
            new P.BodyStyle(TextLevel<A.Level1ParagraphProperties>(1800)),
            new P.OtherStyle(TextLevel<A.Level1ParagraphProperties>(1800))));

    private static P.SlideLayout BuildSlideLayout(long slideWidth) => new(
        new P.CommonSlideData(ShapeTree(TitleShape(slideWidth, text: null))) { Name = "Title Only" },
        new P.ColorMapOverride(new A.MasterColorMapping()))
    {
        Type = P.SlideLayoutValues.TitleOnly,
        Preserve = true,
    };

    // The notes page is portrait 7.5 x 10 in; the slide thumbnail and the notes box below
    // it sit where PowerPoint's own notes master puts them.
    private static P.NotesMaster BuildNotesMaster() => new(
        new P.CommonSlideData(
            WhiteBackground(),
            ShapeTree(
                new P.Shape(
                    NotesPlaceholderProperties(TitleShapeId, "Slide Image", P.PlaceholderValues.SlideImage, index: 2),
                    new P.ShapeProperties(
                        Transform(685800, 1143000, 5486400, 3086100),
                        Rectangle(),
                        new A.Outline(new A.SolidFill(new A.PresetColor { Val = A.PresetColorValues.Black }))
                        {
                            Width = 12700,
                        })),
                new P.Shape(
                    NotesPlaceholderProperties(PictureShapeId, "Notes", P.PlaceholderValues.Body, index: 3),
                    new P.ShapeProperties(Transform(685800, 4400550, 5486400, 3600450), Rectangle()),
                    new P.TextBody(
                        new A.BodyProperties(),
                        new A.ListStyle(),
                        new A.Paragraph(new A.EndParagraphRunProperties { Language = Language }))))),
        ColorMap(),
        new P.NotesStyle(TextLevel<A.Level1ParagraphProperties>(1200)));

    private static P.NotesSlide BuildNotesSlide(string notes)
    {
        var body = new P.TextBody(new A.BodyProperties(), new A.ListStyle());
        foreach (var line in notes.Split('\n'))
        {
            var paragraph = new A.Paragraph();
            var text = line.TrimEnd('\r');
            if (text.Length > 0)
            {
                paragraph.Append(new A.Run(new A.RunProperties { Language = Language }, new A.Text(text)));
            }

            paragraph.Append(new A.EndParagraphRunProperties { Language = Language });
            body.Append(paragraph);
        }

        return new P.NotesSlide(
            new P.CommonSlideData(ShapeTree(
                new P.Shape(
                    NotesPlaceholderProperties(TitleShapeId, "Slide Image", P.PlaceholderValues.SlideImage, index: null),
                    new P.ShapeProperties()),
                new P.Shape(
                    NotesPlaceholderProperties(PictureShapeId, "Notes", P.PlaceholderValues.Body, index: 1),
                    new P.ShapeProperties(),
                    body))),
            new P.ColorMapOverride(new A.MasterColorMapping()));
    }

    private static P.NonVisualShapeProperties NotesPlaceholderProperties(
        uint id,
        string name,
        P.PlaceholderValues type,
        uint? index)
    {
        var locks = type == P.PlaceholderValues.SlideImage
            ? new A.ShapeLocks { NoGrouping = true, NoRotation = true, NoChangeAspect = true }
            : new A.ShapeLocks { NoGrouping = true };
        var placeholder = new P.PlaceholderShape { Type = type };
        if (index is not null)
        {
            placeholder.Index = index.Value;
        }

        return new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualShapeDrawingProperties(locks),
            new P.ApplicationNonVisualDrawingProperties(placeholder));
    }

    private static A.Theme BuildTheme() => new(
        new A.ThemeElements(
            new A.ColorScheme(
                new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
                new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
                new A.Dark2Color(Rgb("44546A")),
                new A.Light2Color(Rgb("E7E6E6")),
                new A.Accent1Color(Rgb("4472C4")),
                new A.Accent2Color(Rgb("ED7D31")),
                new A.Accent3Color(Rgb("A5A5A5")),
                new A.Accent4Color(Rgb("FFC000")),
                new A.Accent5Color(Rgb("5B9BD5")),
                new A.Accent6Color(Rgb("70AD47")),
                new A.Hyperlink(Rgb("0563C1")),
                new A.FollowedHyperlinkColor(Rgb("954F72")))
            {
                Name = "Office",
            },
            new A.FontScheme(ThemeFontSet<A.MajorFont>(), ThemeFontSet<A.MinorFont>()) { Name = "Office" },
            new A.FormatScheme(
                new A.FillStyleList(SchemeFill(), SchemeFill(), SchemeFill()),
                new A.LineStyleList(SchemeLine(6350), SchemeLine(12700), SchemeLine(19050)),
                new A.EffectStyleList(
                    new A.EffectStyle(new A.EffectList()),
                    new A.EffectStyle(new A.EffectList()),
                    new A.EffectStyle(new A.EffectList())),
                new A.BackgroundFillStyleList(SchemeFill(), SchemeFill(), SchemeFill()))
            {
                Name = "Office",
            }))
    {
        Name = "SQLBI Whiteboard",
    };

    private static T ThemeFontSet<T>() where T : OpenXmlCompositeElement, new()
    {
        var fonts = new T();
        fonts.Append(
            new A.LatinFont { Typeface = ThemeFont },
            new A.EastAsianFont { Typeface = "" },
            new A.ComplexScriptFont { Typeface = "" });
        return fonts;
    }

    private static T TextLevel<T>(int fontSize) where T : OpenXmlCompositeElement, new()
    {
        var level = new T();
        level.Append(new A.DefaultRunProperties(new A.LatinFont { Typeface = "+mn-lt" }) { FontSize = fontSize });
        return level;
    }

    // PowerPoint reads the prefixes whether declared once here or on every element, but
    // the SDK only writes them once when the root declares them.
    private static T DeclareNamespaces<T>(T root) where T : OpenXmlPartRootElement
    {
        root.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        root.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        root.AddNamespaceDeclaration("p", "http://schemas.openxmlformats.org/presentationml/2006/main");
        return root;
    }

    private static P.ShapeTree ShapeTree(params OpenXmlElement[] shapes)
    {
        var tree = new P.ShapeTree(
            new P.NonVisualGroupShapeProperties(
                new P.NonVisualDrawingProperties { Id = GroupShapeId, Name = "" },
                new P.NonVisualGroupShapeDrawingProperties(),
                new P.ApplicationNonVisualDrawingProperties()),
            new P.GroupShapeProperties(new A.TransformGroup(
                new A.Offset { X = 0, Y = 0 },
                new A.Extents { Cx = 0, Cy = 0 },
                new A.ChildOffset { X = 0, Y = 0 },
                new A.ChildExtents { Cx = 0, Cy = 0 })));
        tree.Append(shapes);
        return tree;
    }

    private static A.SolidFill SchemeFill() =>
        new(new A.SchemeColor { Val = A.SchemeColorValues.PhColor });

    private static A.Outline SchemeLine(int width) =>
        new(SchemeFill(), new A.PresetDash { Val = A.PresetLineDashValues.Solid })
        {
            Width = width,
            CapType = A.LineCapValues.Flat,
            CompoundLineType = A.CompoundLineValues.Single,
            Alignment = A.PenAlignmentValues.Center,
        };

    private static P.Background WhiteBackground() =>
        new(new P.BackgroundProperties(new A.SolidFill(Rgb("FFFFFF")), new A.EffectList()));

    private static P.ColorMap ColorMap() => new()
    {
        Background1 = A.ColorSchemeIndexValues.Light1,
        Text1 = A.ColorSchemeIndexValues.Dark1,
        Background2 = A.ColorSchemeIndexValues.Light2,
        Text2 = A.ColorSchemeIndexValues.Dark2,
        Accent1 = A.ColorSchemeIndexValues.Accent1,
        Accent2 = A.ColorSchemeIndexValues.Accent2,
        Accent3 = A.ColorSchemeIndexValues.Accent3,
        Accent4 = A.ColorSchemeIndexValues.Accent4,
        Accent5 = A.ColorSchemeIndexValues.Accent5,
        Accent6 = A.ColorSchemeIndexValues.Accent6,
        Hyperlink = A.ColorSchemeIndexValues.Hyperlink,
        FollowedHyperlink = A.ColorSchemeIndexValues.FollowedHyperlink,
    };

    private static A.Transform2D Transform(long x, long y, long width, long height) =>
        new(new A.Offset { X = x, Y = y }, new A.Extents { Cx = width, Cy = height });

    private static A.PresetGeometry Rectangle() =>
        new(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle };

    private static A.RgbColorModelHex Rgb(string hex) => new() { Val = hex };

    // DrawingML has no alpha in the hex; the screen's transparency is dropped.
    private static A.RgbColorModelHex Rgb(uint argb) => Rgb((argb & 0xFFFFFF).ToString("X6"));

    // A fill that is meant to be seen through says so in a child element instead,
    // in thousandths of a percent.
    private static A.RgbColorModelHex Translucent(uint argb)
    {
        var color = Rgb(argb);
        var alpha = argb >> 24;
        if (alpha < 0xFF)
        {
            color.Append(new A.Alpha { Val = (int)Math.Round(alpha * 100000d / 0xFF) });
        }

        return color;
    }
}
