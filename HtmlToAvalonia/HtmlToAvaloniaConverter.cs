using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Css.Dom;
using System.Globalization;
using System.Text.RegularExpressions;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaTextAlignment = Avalonia.Media.TextAlignment;
using AvaloniaFontWeight = Avalonia.Media.FontWeight;
using AvaloniaFontStyle = Avalonia.Media.FontStyle;
using AvaloniaTextDecorationCollection = Avalonia.Media.TextDecorationCollection;

namespace HtmlToAvalonia;

internal class HtmlToAvaloniaConverter
{
    private readonly IDocument _document;

    public HtmlToAvaloniaConverter(IDocument document)
    {
        _document = document;
    }
    public Control ConvertElement(IElement element)
    {
        // Handle different element types
        var tagName = element.TagName.ToLowerInvariant();

        return tagName switch
        {
            "body" or "div" or "span" or "p" => ConvertContainer(element),
            "table" => ConvertTable(element),
            "tr" => ConvertTableRow(element),
            "td" or "th" => ConvertTableCell(element),
            "center" => ConvertCenter(element),
            "b" or "strong" => ConvertBold(element),
            "i" or "em" => ConvertItalic(element),
            "u" => ConvertUnderline(element),
            "br" => new TextBlock { Text = string.Empty },
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => ConvertHeading(element, tagName),
            _ => ConvertTextContent(element)
        };
    }

    private Control ConvertContainer(IElement element)
    {
        var children = element.ChildNodes
            .Where(n => n.NodeType == NodeType.Element || (n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)))
            .ToList();

        if (children.Count == 0)
        {
            return new TextBlock { Text = string.Empty };
        }

        if (children.Count == 1 && children[0].NodeType == NodeType.Text)
        {
            var textBlock = new TextBlock { Text = NormalizeTextNode(children[0].TextContent ?? string.Empty) ?? string.Empty };
            return ApplyStyles(textBlock, element);
        }

        // Check if this container has inline children (text mixed with <b>, <i>, <u>, etc.)
        bool hasInlineChildren = HasInlineChildren(children);

        // Check if there are any block-level elements
        bool hasBlockElements = children.Any(n => n is IElement elem && IsBlockElement(elem));

        // If we have both inline and block elements, we need to use a panel
        if (hasInlineChildren && hasBlockElements)
        {
            // Mixed content: use StackPanel and group consecutive inline elements into TextBlocks
            var panel = new StackPanel();
            var currentInlines = new List<INode>();

            foreach (var child in children)
            {
                if (child is IElement childElement && IsBlockElement(childElement))
                {
                    // Flush any accumulated inline content first
                    if (currentInlines.Count > 0)
                    {
                        var textBlock = CreateTextBlockFromInlines(currentInlines);
                        panel.Children.Add(textBlock);
                        currentInlines.Clear();
                    }

                    // Add the block element
                    panel.Children.Add(ConvertElement(childElement));
                }
                else
                {
                    // Accumulate inline content (text nodes and inline elements)
                    currentInlines.Add(child);
                }
            }

            // Flush any remaining inline content
            if (currentInlines.Count > 0)
            {
                var textBlock = CreateTextBlockFromInlines(currentInlines);
                panel.Children.Add(textBlock);
            }

            return ApplyStyles(panel, element);
        }
        else if (hasInlineChildren)
        {
            // Pure inline content: use TextBlock with Inlines
            var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

            foreach (var child in children)
            {
                if (child.NodeType == NodeType.Element && child is IElement childElement)
                {
                    var inline = ConvertToInline(childElement);
                    if (inline != null)
                    {
                        textBlock.Inlines!.Add(inline);
                    }
                }
                else if (child.NodeType == NodeType.Text)
                {
                    var text = NormalizeTextNode(child.TextContent);
                    if (text != null)
                    {
                        textBlock.Inlines!.Add(new Run(text));
                    }
                }
            }

            return ApplyStyles(textBlock, element);
        }
        else
        {
            // Use StackPanel for block-level children
            Panel panel;
            if (element is IHtmlElement htmlElement)
            {
                var window = _document.DefaultView;
                if (window != null)
                {
                    var computedStyle = window.GetComputedStyle(htmlElement);
                    var display = computedStyle?.GetPropertyValue("display");

                    // Use WrapPanel for inline elements, StackPanel for block elements
                    if (display == "inline" || display == "inline-block")
                    {
                        panel = new WrapPanel();
                    }
                    else
                    {
                        panel = new StackPanel();
                    }
                }
                else
                {
                    panel = new StackPanel();
                }
            }
            else
            {
                panel = new StackPanel();
            }

            foreach (var child in children)
            {
                if (child.NodeType == NodeType.Element && child is IElement childElement)
                {
                    var control = ConvertElement(childElement);
                    panel.Children.Add(control);
                }
                else if (child.NodeType == NodeType.Text)
                {
                    var text = child.TextContent?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        var textBlock = new TextBlock { Text = text };
                        panel.Children.Add(textBlock);
                    }
                }
            }

            return ApplyStyles(panel, element);
        }
    }

    private bool HasInlineChildren(List<INode> children)
    {
        // Check if any children are inline formatting elements or if there's mixed text and elements
        bool hasText = children.Any(n => n.NodeType == NodeType.Text);
        bool hasInlineElements = children.Any(n =>
        {
            if (n is IElement elem)
            {
                var tag = elem.TagName.ToLowerInvariant();
                return tag == "b" || tag == "strong" || tag == "i" || tag == "em" ||
                       tag == "u" || tag == "span" || tag == "a" || tag == "br";
            }
            return false;
        });

        // If we have both text and inline elements, or just inline elements mixed with text, use Inlines
        return hasText && hasInlineElements;
    }

    private bool IsBlockElement(IElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        return tag == "div" || tag == "p" || tag == "h1" || tag == "h2" || tag == "h3" ||
               tag == "h4" || tag == "h5" || tag == "h6" || tag == "table" || tag == "center";
    }

    private TextBlock CreateTextBlockFromInlines(List<INode> inlineNodes)
    {
        var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap };

        foreach (var node in inlineNodes)
        {
            if (node.NodeType == NodeType.Element && node is IElement element)
            {
                var inline = ConvertToInline(element);
                if (inline != null)
                {
                    textBlock.Inlines!.Add(inline);
                }
            }
            else if (node.NodeType == NodeType.Text)
            {
                var text = NormalizeTextNode(node.TextContent);
                if (text != null)
                {
                    textBlock.Inlines!.Add(new Run(text));
                }
            }
        }

        return textBlock;
    }

    // Helper class to track accumulated formatting through nested tags
    private class InlineFormattingContext
    {
        public AvaloniaFontWeight FontWeight { get; set; } = AvaloniaFontWeight.Normal;
        public AvaloniaFontStyle FontStyle { get; set; } = AvaloniaFontStyle.Normal;
        public AvaloniaTextDecorationCollection? TextDecorations { get; set; } = null;

        public InlineFormattingContext Clone()
        {
            return new InlineFormattingContext
            {
                FontWeight = this.FontWeight,
                FontStyle = this.FontStyle,
                TextDecorations = this.TextDecorations
            };
        }
    }

    private Inline? ConvertToInline(IElement element)
    {
        return ConvertToInline(element, new InlineFormattingContext());
    }

    private Inline? ConvertToInline(IElement element, InlineFormattingContext parentContext)
    {
        var tagName = element.TagName.ToLowerInvariant();

        switch (tagName)
        {
            case "b" or "strong":
                return CreateBoldInline(element, parentContext);
            case "i" or "em":
                return CreateItalicInline(element, parentContext);
            case "u":
                return CreateUnderlineInline(element, parentContext);
            case "span":
                return CreateSpanInline(element, parentContext);
            case "br":
                return new LineBreak();
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                return CreateHeadingInline(element, tagName, parentContext);
            case "p":
                return CreateParagraphInline(element, parentContext);
            default:
                // For unknown inline elements, just return the text content
                return new Run(element.TextContent);
        }
    }

    /// <summary>
    /// Helper method to create a Span with formatting from the context.
    /// </summary>
    private Span CreateFormattedSpan(InlineFormattingContext context, double? fontSize = null)
    {
        var span = new Span
        {
            FontWeight = context.FontWeight,
            FontStyle = context.FontStyle,
            TextDecorations = context.TextDecorations
        };

        if (fontSize.HasValue)
        {
            span.FontSize = fontSize.Value;
        }

        return span;
    }

    private Inline CreateBoldInline(IElement element, InlineFormattingContext parentContext)
    {
        // Clone parent context and add bold
        var context = parentContext.Clone();
        context.FontWeight = AvaloniaFontWeight.Bold;

        var span = CreateFormattedSpan(context);
        ApplyInlineStyles(span, element, context, preserveFontWeight: true);
        AddInlineContent(span, element, context);
        return span;
    }

    private Inline CreateItalicInline(IElement element, InlineFormattingContext parentContext)
    {
        // Clone parent context and add italic
        var context = parentContext.Clone();
        context.FontStyle = AvaloniaFontStyle.Italic;

        var span = CreateFormattedSpan(context);
        ApplyInlineStyles(span, element, context, preserveFontStyle: true);
        AddInlineContent(span, element, context);
        return span;
    }

    private Inline CreateUnderlineInline(IElement element, InlineFormattingContext parentContext)
    {
        // Clone parent context and add underline
        var context = parentContext.Clone();
        context.TextDecorations = TextDecorations.Underline;

        var span = CreateFormattedSpan(context);
        ApplyInlineStyles(span, element, context, preserveTextDecoration: true);
        AddInlineContent(span, element, context);
        return span;
    }

    private Inline CreateSpanInline(IElement element, InlineFormattingContext parentContext)
    {
        // Clone parent context (no additional formatting)
        var context = parentContext.Clone();

        var span = CreateFormattedSpan(context);
        ApplyInlineStyles(span, element, context);
        AddInlineContent(span, element, context);
        return span;
    }

    private Inline CreateHeadingInline(IElement element, string tagName, InlineFormattingContext parentContext)
    {
        // Clone parent context and add bold
        var context = parentContext.Clone();
        context.FontWeight = AvaloniaFontWeight.Bold;

        var span = CreateFormattedSpan(context, GetHeadingFontSize(tagName));
        ApplyInlineStyles(span, element, context, preserveFontWeight: true);
        AddInlineContent(span, element, context);
        return span;
    }

    private Inline CreateParagraphInline(IElement element, InlineFormattingContext parentContext)
    {
        // Clone parent context (no additional formatting for paragraphs)
        var context = parentContext.Clone();

        // Wrap the paragraph content in a Span, preceded by a blank line to mimic block spacing.
        // A <p> is equivalent to two line breaks before its content.
        var wrapper = new Span();
        wrapper.Inlines.Add(new LineBreak());
        wrapper.Inlines.Add(new LineBreak());

        var inner = CreateFormattedSpan(context);
        ApplyInlineStyles(inner, element, context);
        AddInlineContent(inner, element, context);

        wrapper.Inlines.Add(inner);
        return wrapper;
    }

    private void AddInlineContent(Span parentSpan, IElement element, InlineFormattingContext context)
    {
        foreach (var child in element.ChildNodes)
        {
            if (child.NodeType == NodeType.Element && child is IElement childElement)
            {
                // Pass the context down to child elements so they accumulate formatting
                var inline = ConvertToInline(childElement, context);
                if (inline != null)
                {
                    parentSpan.Inlines.Add(inline);
                }
            }
            else if (child.NodeType == NodeType.Text)
            {
                var text = NormalizeTextNode(child.TextContent);
                if (text != null)
                {
                    // Apply accumulated formatting from context to text runs
                    var run = new Run(text)
                    {
                        FontWeight = context.FontWeight,
                        FontStyle = context.FontStyle,
                        TextDecorations = context.TextDecorations
                    };

                    parentSpan.Inlines.Add(run);
                }
            }
        }
    }

    private void ApplyInlineStyles(Span span, IElement element, InlineFormattingContext? context = null, bool preserveFontWeight = false, bool preserveFontStyle = false, bool preserveTextDecoration = false)
    {
        // Use GetComputedStyle to get all computed CSS properties
        if (element is IHtmlElement htmlElement)
        {
            var window = _document.DefaultView;
            if (window != null)
            {
                var computedStyle = window.GetComputedStyle(htmlElement);
                if (computedStyle != null)
                {
                    // Apply color
                    var color = computedStyle.GetPropertyValue("color");
                    if (!string.IsNullOrEmpty(color))
                    {
                        var brush = ParseColor(color);
                        if (brush != null)
                        {
                            span.Foreground = brush;
                        }
                    }

                    // Apply background-color
                    var backgroundColor = computedStyle.GetPropertyValue("background-color");
                    if (!string.IsNullOrEmpty(backgroundColor))
                    {
                        var bgBrush = ParseColor(backgroundColor);
                        if (bgBrush != null)
                        {
                            span.Background = bgBrush;
                        }
                    }

                    // Apply font-size
                    var fontSize = computedStyle.GetPropertyValue("font-size");
                    if (!string.IsNullOrEmpty(fontSize))
                    {
                        span.FontSize = ParseFontSize(fontSize);
                    }

                    // Apply font-weight (only if not preserving the already-set value)
                    if (!preserveFontWeight)
                    {
                        var fontWeight = computedStyle.GetPropertyValue("font-weight");
                        if (!string.IsNullOrEmpty(fontWeight))
                        {
                            span.FontWeight = ParseFontWeight(fontWeight);
                            if (context != null) context.FontWeight = span.FontWeight;
                        }
                    }

                    // Apply font-style (only if not preserving the already-set value)
                    if (!preserveFontStyle)
                    {
                        var fontStyle = computedStyle.GetPropertyValue("font-style");
                        if (!string.IsNullOrEmpty(fontStyle))
                        {
                            span.FontStyle = ParseFontStyle(fontStyle);
                            if (context != null) context.FontStyle = span.FontStyle;
                        }
                    }

                    // Apply text-decoration (only if not preserving the already-set value)
                    if (!preserveTextDecoration)
                    {
                        var textDecoration = computedStyle.GetPropertyValue("text-decoration");
                        if (!string.IsNullOrEmpty(textDecoration) && textDecoration.ToLowerInvariant().Contains("underline"))
                        {
                            span.TextDecorations = TextDecorations.Underline;
                            if (context != null) context.TextDecorations = span.TextDecorations;
                        }
                    }
                }
            }
        }
    }

    private Control ConvertTable(IElement element)
    {
        var grid = new Grid();
        var rows = element.QuerySelectorAll("tr").ToList();

        // Pre-scan to determine column count (accounting for colspan and rowspan)
        int maxColCount = 0;
        var occupiedCells = new Dictionary<(int row, int col), bool>();

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.QuerySelectorAll("td, th").ToList();
            int colIndex = 0;

            foreach (var cell in cells)
            {
                // Skip columns occupied by rowspanned cells from previous rows
                while (occupiedCells.ContainsKey((rowIndex, colIndex)))
                {
                    colIndex++;
                }

                var colspanAttr = cell.GetAttribute("colspan");
                int colspan = 1;
                if (!string.IsNullOrEmpty(colspanAttr) && int.TryParse(colspanAttr, out var parsedColspan))
                {
                    colspan = parsedColspan;
                }

                var rowspanAttr = cell.GetAttribute("rowspan");
                int rowspan = 1;
                if (!string.IsNullOrEmpty(rowspanAttr) && int.TryParse(rowspanAttr, out var parsedRowspan))
                {
                    rowspan = parsedRowspan;
                }

                // Mark cells as occupied by this cell's colspan and rowspan
                for (int r = 0; r < rowspan; r++)
                {
                    for (int c = 0; c < colspan; c++)
                    {
                        occupiedCells[(rowIndex + r, colIndex + c)] = true;
                    }
                }

                colIndex += colspan;
                if (colIndex > maxColCount)
                {
                    maxColCount = colIndex;
                }
            }
        }

        // Define rows and columns
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
        for (int i = 0; i < maxColCount; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        // Reset occupied cells tracker for actual placement
        occupiedCells.Clear();

        // Process each row and cell
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            var cells = row.QuerySelectorAll("td, th").ToList();
            int colIndex = 0;

            foreach (var cell in cells)
            {
                // Skip columns occupied by rowspanned cells from previous rows
                while (occupiedCells.ContainsKey((rowIndex, colIndex)))
                {
                    colIndex++;
                }

                var cellControl = ConvertTableCell(cell);

                Grid.SetRow(cellControl, rowIndex);
                Grid.SetColumn(cellControl, colIndex);

                // Handle rowspan
                var rowspanAttr = cell.GetAttribute("rowspan");
                int rowspan = 1;
                if (!string.IsNullOrEmpty(rowspanAttr) && int.TryParse(rowspanAttr, out var parsedRowspan) && parsedRowspan > 1)
                {
                    rowspan = parsedRowspan;
                    Grid.SetRowSpan(cellControl, rowspan);
                }

                // Handle colspan
                var colspanAttr = cell.GetAttribute("colspan");
                int colspan = 1;
                if (!string.IsNullOrEmpty(colspanAttr) && int.TryParse(colspanAttr, out var parsedColspan) && parsedColspan > 1)
                {
                    colspan = parsedColspan;
                    Grid.SetColumnSpan(cellControl, colspan);
                }

                // Mark cells as occupied by this cell's colspan and rowspan
                for (int r = 0; r < rowspan; r++)
                {
                    for (int c = 0; c < colspan; c++)
                    {
                        occupiedCells[(rowIndex + r, colIndex + c)] = true;
                    }
                }

                grid.Children.Add(cellControl);
                colIndex += colspan;
            }
        }

        return ApplyStyles(grid, element);
    }

    private Control ConvertTableRow(IElement element)
    {
        // Table rows are handled by ConvertTable
        return ConvertContainer(element);
    }

    private Control ConvertTableCell(IElement element)
    {
        var border = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5)
        };

        var children = element.ChildNodes
            .Where(n => n.NodeType == NodeType.Element || (n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)))
            .ToList();

        if (children.Count == 1 && children[0].NodeType == NodeType.Text)
        {
            var textBlock = new TextBlock
            {
                Text = children[0].TextContent?.Trim() ?? string.Empty,
                TextWrapping = TextWrapping.Wrap
            };

            if (element.TagName.ToLowerInvariant() == "th")
            {
                textBlock.FontWeight = AvaloniaFontWeight.Bold;
            }

            border.Child = textBlock;
        }
        else
        {
            var panel = new StackPanel();
            foreach (var child in children)
            {
                if (child.NodeType == NodeType.Element && child is IElement childElement)
                {
                    panel.Children.Add(ConvertElement(childElement));
                }
                else if (child.NodeType == NodeType.Text)
                {
                    var text = child.TextContent?.Trim();
                    if (!string.IsNullOrEmpty(text))
                    {
                        panel.Children.Add(new TextBlock { Text = text });
                    }
                }
            }
            border.Child = panel;
        }

        return ApplyStyles(border, element);
    }

    private Control ConvertCenter(IElement element)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = AvaloniaHorizontalAlignment.Center
        };

        foreach (var child in element.ChildNodes)
        {
            if (child.NodeType == NodeType.Element && child is IElement childElement)
            {
                var control = ConvertElement(childElement);
                panel.Children.Add(control);
            }
            else if (child.NodeType == NodeType.Text)
            {
                var text = child.TextContent?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var textBlock = new TextBlock
                    {
                        Text = text,
                        TextAlignment = AvaloniaTextAlignment.Center
                    };
                    panel.Children.Add(textBlock);
                }
            }
        }

        return ApplyStyles(panel, element);
    }

    private Control ConvertBold(IElement element)
    {
        var textBlock = new TextBlock
        {
            Text = element.TextContent,
            FontWeight = AvaloniaFontWeight.Bold
        };
        return ApplyStyles(textBlock, element);
    }

    private Control ConvertItalic(IElement element)
    {
        var textBlock = new TextBlock
        {
            Text = element.TextContent,
            FontStyle = AvaloniaFontStyle.Italic
        };
        return ApplyStyles(textBlock, element);
    }

    private Control ConvertUnderline(IElement element)
    {
        var textBlock = new TextBlock
        {
            Text = element.TextContent,
            TextDecorations = TextDecorations.Underline
        };
        return ApplyStyles(textBlock, element);
    }

    private Control ConvertHeading(IElement element, string tagName)
    {
        var children = element.ChildNodes
            .Where(n => n.NodeType == NodeType.Element || (n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)))
            .ToList();

        // Check if heading has inline children (like <b>, <i>, etc.)
        bool hasInlineChildren = HasInlineChildren(children);

        var textBlock = new TextBlock
        {
            FontWeight = AvaloniaFontWeight.Bold,
            FontSize = GetHeadingFontSize(tagName),
            TextWrapping = TextWrapping.Wrap
        };

        if (hasInlineChildren)
        {
            // Use Inlines for mixed content with inline formatting
            // Create a formatting context with the heading's bold weight
            var context = new InlineFormattingContext
            {
                FontWeight = AvaloniaFontWeight.Bold
            };

            foreach (var child in children)
            {
                if (child.NodeType == NodeType.Element && child is IElement childElement)
                {
                    var inline = ConvertToInline(childElement, context);
                    if (inline != null)
                    {
                        textBlock.Inlines!.Add(inline);
                    }
                }
                else if (child.NodeType == NodeType.Text)
                {
                    var text = child.TextContent;
                    if (!string.IsNullOrEmpty(text))
                    {
                        textBlock.Inlines!.Add(new Run(text) { FontWeight = AvaloniaFontWeight.Bold });
                    }
                }
            }
        }
        else
        {
            // Simple text content
            textBlock.Text = element.TextContent;
        }

        return ApplyStyles(textBlock, element);
    }

    private Control ConvertTextContent(IElement element)
    {
        var textBlock = new TextBlock
        {
            Text = NormalizeTextNode(element.TextContent ?? string.Empty) ?? string.Empty
        };
        return ApplyStyles(textBlock, element);
    }

    private Control ApplyStyles(Control control, IElement element)
    {
        Control resultControl = control;
        double? widthPercentage = null;
        double? heightPercentage = null;

        // Use GetComputedStyle to get all computed CSS properties
        if (element is IHtmlElement htmlElement)
        {
            var window = _document.DefaultView;
            if (window != null)
            {
                var computedStyle = window.GetComputedStyle(htmlElement);
                if (computedStyle != null)
                {
                    ApplyComputedStyles(control, computedStyle, htmlElement, out widthPercentage, out heightPercentage);
                }
            }
        }

        // Apply align attribute (legacy HTML) - this takes precedence
        var alignAttr = element.GetAttribute("align");
        if (!string.IsNullOrEmpty(alignAttr))
        {
            ApplyAlignment(control, alignAttr);
        }

        // Wrap in Grid if percentage width or height is specified
        if (widthPercentage.HasValue || heightPercentage.HasValue)
        {
            resultControl = WrapInPercentageGrid(control, widthPercentage, heightPercentage);
        }

        // Set the Name property if the element has an id
        var id = element.Id;
        if (!string.IsNullOrEmpty(id))
        {
            resultControl.Name = id;
        }

        return resultControl;
    }

    private Control WrapInPercentageGrid(Control control, double? widthPercentage, double? heightPercentage)
    {
        var grid = new Grid();

        // Set up column definitions for width percentage
        if (widthPercentage.HasValue && widthPercentage.Value < 100)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(widthPercentage.Value, GridUnitType.Star)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(100 - widthPercentage.Value, GridUnitType.Star)));
            Grid.SetColumn(control, 0);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        // Set up row definitions for height percentage
        if (heightPercentage.HasValue && heightPercentage.Value < 100)
        {
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(heightPercentage.Value, GridUnitType.Star)));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(100 - heightPercentage.Value, GridUnitType.Star)));
            Grid.SetRow(control, 0);
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }

        // Make the control stretch within its grid cell
        control.HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

        grid.Children.Add(control);
        return grid;
    }

    private void ApplyComputedStyles(Control control, ICssStyleDeclaration computedStyle, IHtmlElement htmlElement, out double? widthPercentage, out double? heightPercentage)
    {
        widthPercentage = null;
        heightPercentage = null;

        // Apply color
        var color = computedStyle.GetPropertyValue("color");
        if (!string.IsNullOrEmpty(color))
        {
            ApplyCssProperty(control, "color", color);
        }

        // Apply background-color
        var backgroundColor = computedStyle.GetPropertyValue("background-color");
        if (!string.IsNullOrEmpty(backgroundColor))
        {
            ApplyCssProperty(control, "background-color", backgroundColor);
        }

        // Apply font-size
        var fontSize = computedStyle.GetPropertyValue("font-size");
        if (!string.IsNullOrEmpty(fontSize))
        {
            ApplyCssProperty(control, "font-size", fontSize);
        }

        // Apply font-weight
        var fontWeight = computedStyle.GetPropertyValue("font-weight");
        if (!string.IsNullOrEmpty(fontWeight))
        {
            ApplyCssProperty(control, "font-weight", fontWeight);
        }

        // Apply font-style
        var fontStyle = computedStyle.GetPropertyValue("font-style");
        if (!string.IsNullOrEmpty(fontStyle))
        {
            ApplyCssProperty(control, "font-style", fontStyle);
        }

        // Apply text-align
        var textAlign = computedStyle.GetPropertyValue("text-align");
        if (!string.IsNullOrEmpty(textAlign))
        {
            ApplyCssProperty(control, "text-align", textAlign);
        }

        // Apply text-decoration
        var textDecoration = computedStyle.GetPropertyValue("text-decoration");
        if (!string.IsNullOrEmpty(textDecoration))
        {
            ApplyCssProperty(control, "text-decoration", textDecoration);
        }

        // Apply padding
        var paddingTop    = ParseLength(computedStyle.GetPropertyValue("padding-top"));
        var paddingRight  = ParseLength(computedStyle.GetPropertyValue("padding-right"));
        var paddingBottom = ParseLength(computedStyle.GetPropertyValue("padding-bottom"));
        var paddingLeft   = ParseLength(computedStyle.GetPropertyValue("padding-left"));
        if (paddingTop != 0 || paddingRight != 0 || paddingBottom != 0 || paddingLeft != 0)
        {
            var thickness = new Thickness(paddingLeft, paddingTop, paddingRight, paddingBottom);
            if (control is Border borderPad)
                borderPad.Padding = thickness;
            else if (control is Decorator decoratorPad)
                decoratorPad.Padding = thickness;
        }

        // Apply margin
        var marginTop    = ParseLength(computedStyle.GetPropertyValue("margin-top"));
        var marginRight  = ParseLength(computedStyle.GetPropertyValue("margin-right"));
        var marginBottom = ParseLength(computedStyle.GetPropertyValue("margin-bottom"));
        var marginLeft   = ParseLength(computedStyle.GetPropertyValue("margin-left"));
        if (marginTop != 0 || marginRight != 0 || marginBottom != 0 || marginLeft != 0)
        {
            control.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
        }

        // Apply width - check inline style for percentage values using AngleSharp's GetStyle() API
        var width = computedStyle.GetPropertyValue("width");
        if (!string.IsNullOrEmpty(width) && width != "auto")
        {
            // Get the inline style declaration using AngleSharp's GetStyle() method
            var inlineStyle = htmlElement.GetStyle();
            var inlineWidth = inlineStyle?.GetPropertyValue("width");
            if (!string.IsNullOrEmpty(inlineWidth) && inlineWidth.Contains("%"))
            {
                // Extract percentage value for wrapping
                var percentageStr = inlineWidth.Replace("%", "").Trim();
                if (double.TryParse(percentageStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var percentage))
                {
                    widthPercentage = percentage;
                }
            }
            else
            {
                // Use the computed pixel value
                ApplyCssProperty(control, "width", width);
            }
        }

        // Apply height - check inline style for percentage values using AngleSharp's GetStyle() API
        var height = computedStyle.GetPropertyValue("height");
        if (!string.IsNullOrEmpty(height) && height != "auto")
        {
            // Get the inline style declaration using AngleSharp's GetStyle() method
            var inlineStyle = htmlElement.GetStyle();
            var inlineHeight = inlineStyle?.GetPropertyValue("height");
            if (!string.IsNullOrEmpty(inlineHeight) && inlineHeight.Contains("%"))
            {
                // Extract percentage value for wrapping
                var percentageStr = inlineHeight.Replace("%", "").Trim();
                if (double.TryParse(percentageStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var percentage))
                {
                    heightPercentage = percentage;
                }
            }
            else
            {
                // Use the computed pixel value
                ApplyCssProperty(control, "height", height);
            }
        }
    }

    private void ApplyCssProperty(Control control, string property, string value)
    {
        switch (property.ToLowerInvariant())
        {
            case "color":
                if (control is TextBlock textBlock)
                {
                    var brush = ParseColor(value);
                    if (brush != null)
                    {
                        textBlock.Foreground = brush;
                    }
                }
                break;

            case "background-color":
                var bgBrush = ParseColor(value);
                if (bgBrush != null)
                {
                    if (control is Border border)
                    {
                        border.Background = bgBrush;
                    }
                    else if (control is Panel panel)
                    {
                        panel.Background = bgBrush;
                    }
                }
                break;

            case "font-size":
                if (control is TextBlock tb)
                {
                    tb.FontSize = ParseFontSize(value);
                }
                break;

            case "font-weight":
                if (control is TextBlock tb2)
                    tb2.FontWeight = ParseFontWeight(value);
                break;

            case "font-style":
                if (control is TextBlock tb3)
                    tb3.FontStyle = ParseFontStyle(value);
                break;

            case "text-align":
                ApplyTextAlign(control, value);
                break;

            case "text-decoration":
                if (control is TextBlock tb4 && value.ToLowerInvariant().Contains("underline"))
                {
                    tb4.TextDecorations = TextDecorations.Underline;
                }
                break;

            case "width":
                // Only handle pixel values here; percentages are handled in ApplyComputedStyles
                if (double.TryParse(value.Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var width))
                {
                    control.Width = width;
                }
                break;

            case "height":
                // Only handle pixel values here; percentages are handled in ApplyComputedStyles
                if (double.TryParse(value.Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var height))
                {
                    control.Height = height;
                }
                break;
        }
    }

    private void ApplyAlignment(Control control, string align)
    {
        var alignment = align.ToLowerInvariant();

        if (control is TextBlock textBlock)
        {
            textBlock.TextAlignment = alignment switch
            {
                "left" => AvaloniaTextAlignment.Left,
                "center" => AvaloniaTextAlignment.Center,
                "right" => AvaloniaTextAlignment.Right,
                "justify" => AvaloniaTextAlignment.Justify,
                _ => AvaloniaTextAlignment.Left
            };
        }

        control.HorizontalAlignment = alignment switch
        {
            "left" => AvaloniaHorizontalAlignment.Left,
            "center" => AvaloniaHorizontalAlignment.Center,
            "right" => AvaloniaHorizontalAlignment.Right,
            _ => AvaloniaHorizontalAlignment.Stretch
        };
    }

    private void ApplyTextAlign(Control control, string value)
    {
        var alignment = value.ToLowerInvariant();

        if (control is TextBlock textBlock)
        {
            textBlock.TextAlignment = alignment switch
            {
                "left" => AvaloniaTextAlignment.Left,
                "center" => AvaloniaTextAlignment.Center,
                "right" => AvaloniaTextAlignment.Right,
                "justify" => AvaloniaTextAlignment.Justify,
                _ => AvaloniaTextAlignment.Left
            };
        }

        if (control is Panel panel)
        {
            panel.HorizontalAlignment = alignment switch
            {
                "left" => AvaloniaHorizontalAlignment.Left,
                "center" => AvaloniaHorizontalAlignment.Center,
                "right" => AvaloniaHorizontalAlignment.Right,
                _ => AvaloniaHorizontalAlignment.Stretch
            };
        }
    }

    private IBrush? ParseColor(string? colorValue)
    {
        if (string.IsNullOrEmpty(colorValue) ||
            colorValue.Equals("inherit", StringComparison.OrdinalIgnoreCase) ||
            colorValue.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return null; // Let Avalonia handle inheritance or transparency
        }

        try
        {
            // SolidColorBrush.Parse can handle named colors, hex, rgb(), rgba(), etc.
            return SolidColorBrush.Parse(colorValue);
        }
        catch (FormatException)
        {
            // If parsing fails, return null to use the default color
            return null;
        }
    }

    private double ParseFontSize(string fontSize)
    {
        fontSize = fontSize.ToLowerInvariant().Trim();

        // Handle pixel values
        if (fontSize.EndsWith("px"))
        {
            if (double.TryParse(fontSize.Replace("px", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var px))
            {
                return px;
            }
        }

        // Handle point values
        if (fontSize.EndsWith("pt"))
        {
            if (double.TryParse(fontSize.Replace("pt", "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
            {
                return pt * 1.333; // Convert pt to px
            }
        }

        // Handle named sizes
        return fontSize switch
        {
            "xx-small" => 9,
            "x-small" => 10,
            "small" => 13,
            "medium" => 16,
            "large" => 18,
            "x-large" => 24,
            "xx-large" => 32,
            _ => 14
        };
    }

    private Thickness ParseThickness(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var values = parts.Select(p => ParseLength(p)).ToArray();

        return values.Length switch
        {
            1 => new Thickness(values[0]),
            2 => new Thickness(values[0], values[1], values[0], values[1]),
            3 => new Thickness(values[0], values[1], values[0], values[2]),
            4 => new Thickness(values[0], values[1], values[2], values[3]),
            _ => new Thickness(0)
        };
    }

    /// <summary>
    /// Normalizes an HTML text node's whitespace the same way browsers do:
    /// collapses all runs of whitespace (including newlines) to a single space.
    /// Returns null when the result is empty so callers can skip adding a Run.
    /// </summary>
    private static string? NormalizeTextNode(string raw)
    {
        // Collapse any whitespace sequence (spaces, tabs, \r, \n) to a single space
        var collapsed = Regex.Replace(raw, @"[\s]+", " ");
        return collapsed.Length == 0 ? null : collapsed;
    }

    private double ParseLength(string length)
    {
        length = length.ToLowerInvariant().Trim();
        length = length.Replace("px", "").Replace("pt", "");

        if (double.TryParse(length, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return 0;
    }

    /// <summary>
    /// Parses a CSS font-weight value to an Avalonia FontWeight.
    /// </summary>
    private AvaloniaFontWeight ParseFontWeight(string fontWeight)
    {
        if (string.IsNullOrEmpty(fontWeight))
            return AvaloniaFontWeight.Normal;

        fontWeight = fontWeight.ToLowerInvariant().Trim();

        return fontWeight == "bold" || fontWeight == "700" || fontWeight == "800" || fontWeight == "900"
            ? AvaloniaFontWeight.Bold
            : AvaloniaFontWeight.Normal;
    }

    /// <summary>
    /// Parses a CSS font-style value to an Avalonia FontStyle.
    /// </summary>
    private AvaloniaFontStyle ParseFontStyle(string fontStyle)
    {
        if (string.IsNullOrEmpty(fontStyle))
            return AvaloniaFontStyle.Normal;

        return fontStyle.ToLowerInvariant().Trim() == "italic"
            ? AvaloniaFontStyle.Italic
            : AvaloniaFontStyle.Normal;
    }

    /// <summary>
    /// Gets the font size for a heading tag (h1-h6).
    /// </summary>
    private double GetHeadingFontSize(string tagName)
    {
        return tagName.ToLowerInvariant() switch
        {
            "h1" => 32,
            "h2" => 24,
            "h3" => 18.72,
            "h4" => 16,
            "h5" => 13.28,
            "h6" => 10.72,
            _ => 14
        };
    }

    /// <summary>
    /// Applies inline HTML content to an existing TextBlock.
    /// Only processes inline elements and text content, ignoring block-level elements.
    /// </summary>
    public void ApplyInlineContentToTextBlock(TextBlock textBlock, IElement element)
    {
        // Clear any existing content
        textBlock.Inlines?.Clear();
        textBlock.Text = null;

        // Set text wrapping
        textBlock.TextWrapping = TextWrapping.Wrap;

        // Process all child nodes and extract inline content
        ProcessInlineContent(textBlock, element);

        // Apply styles from the root element to the TextBlock itself
        ApplyTextBlockStyles(textBlock, element);
    }

    /// <summary>
    /// Recursively processes nodes and adds inline content to the TextBlock.
    /// </summary>
    private void ProcessInlineContent(TextBlock textBlock, INode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == NodeType.Element && child is IElement childElement)
            {
                var tagName = childElement.TagName.ToLowerInvariant();

                // Only process inline elements
                if (IsInlineElement(childElement))
                {
                    var inline = ConvertToInline(childElement);
                    if (inline != null)
                    {
                        textBlock.Inlines!.Add(inline);
                    }
                }
                else
                {
                    // For block elements, recursively process their children to extract inline content
                    ProcessInlineContent(textBlock, childElement);
                }
            }
            else if (child.NodeType == NodeType.Text)
            {
                var text = NormalizeTextNode(child.TextContent);
                if (text != null)
                {
                    textBlock.Inlines!.Add(new Run(text));
                }
            }
        }
    }

    /// <summary>
    /// Checks if an element can be represented as inline content in a TextBlock.
    /// This includes true inline elements (b, i, u, span) and block-level elements
    /// that can be represented inline (h1-h6, p).
    /// </summary>
    private bool IsInlineElement(IElement element)
    {
        var tag = element.TagName.ToLowerInvariant();
        return tag == "b" || tag == "strong" || tag == "i" || tag == "em" ||
               tag == "u" || tag == "span" || tag == "a" || tag == "br" ||
               tag == "h1" || tag == "h2" || tag == "h3" || tag == "h4" || tag == "h5" || tag == "h6" ||
               tag == "p";
    }

    /// <summary>
    /// Applies styles from the HTML element to the TextBlock control.
    /// </summary>
    private void ApplyTextBlockStyles(TextBlock textBlock, IElement element)
    {
        // Use GetComputedStyle to get all computed CSS properties
        if (element is IHtmlElement htmlElement)
        {
            var window = _document.DefaultView;
            if (window != null)
            {
                var computedStyle = window.GetComputedStyle(htmlElement);
                if (computedStyle != null)
                {
                    // Apply color
                    var color = computedStyle.GetPropertyValue("color");
                    if (!string.IsNullOrEmpty(color))
                    {
                        var brush = ParseColor(color);
                        if (brush != null)
                        {
                            textBlock.Foreground = brush;
                        }
                    }

                    // Apply background-color
                    var backgroundColor = computedStyle.GetPropertyValue("background-color");
                    if (!string.IsNullOrEmpty(backgroundColor) &&
                        !backgroundColor.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                    {
                        var brush = ParseColor(backgroundColor);
                        if (brush != null)
                        {
                            textBlock.Background = brush;
                        }
                    }

                    // Apply font-size
                    var fontSize = computedStyle.GetPropertyValue("font-size");
                    if (!string.IsNullOrEmpty(fontSize))
                    {
                        var size = ParseLength(fontSize);
                        if (size > 0)
                        {
                            textBlock.FontSize = size;
                        }
                    }

                    // Apply font-weight
                    var fontWeight = computedStyle.GetPropertyValue("font-weight");
                    if (!string.IsNullOrEmpty(fontWeight))
                    {
                        textBlock.FontWeight = ParseFontWeight(fontWeight);
                    }

                    // Apply font-style
                    var fontStyle = computedStyle.GetPropertyValue("font-style");
                    if (!string.IsNullOrEmpty(fontStyle))
                    {
                        textBlock.FontStyle = ParseFontStyle(fontStyle);
                    }

                    // Apply text-align
                    var textAlign = computedStyle.GetPropertyValue("text-align");
                    if (!string.IsNullOrEmpty(textAlign))
                    {
                        textBlock.TextAlignment = textAlign switch
                        {
                            "left" => AvaloniaTextAlignment.Left,
                            "center" => AvaloniaTextAlignment.Center,
                            "right" => AvaloniaTextAlignment.Right,
                            "justify" => AvaloniaTextAlignment.Justify,
                            _ => AvaloniaTextAlignment.Left
                        };
                    }
                }
            }
        }
    }
}

