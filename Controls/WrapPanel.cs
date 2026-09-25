using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace OfflineWinDict.Controls
{
    /// <summary>A lightweight horizontal wrap panel for chip rows (synonyms, topics).</summary>
    public sealed class WrapPanel : Panel
    {
        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            nameof(Spacing), typeof(double), typeof(WrapPanel), new PropertyMetadata(8.0));

        public double Spacing
        {
            get => (double)GetValue(SpacingProperty);
            set => SetValue(SpacingProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
            double x = 0, y = 0, rowHeight = 0, maxRowWidth = 0;

            foreach (var child in Children)
            {
                child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = child.DesiredSize;

                if (x > 0 && x + size.Width > width)
                {
                    maxRowWidth = Math.Max(maxRowWidth, x - Spacing);
                    x = 0;
                    y += rowHeight + Spacing;
                    rowHeight = 0;
                }

                x += size.Width + Spacing;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            maxRowWidth = Math.Max(maxRowWidth, x > 0 ? x - Spacing : 0);
            return new Size(double.IsInfinity(width) ? maxRowWidth : width, y + rowHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0, y = 0, rowHeight = 0;

            foreach (var child in Children)
            {
                var size = child.DesiredSize;

                if (x > 0 && x + size.Width > finalSize.Width)
                {
                    x = 0;
                    y += rowHeight + Spacing;
                    rowHeight = 0;
                }

                child.Arrange(new Rect(x, y, size.Width, size.Height));
                x += size.Width + Spacing;
                rowHeight = Math.Max(rowHeight, size.Height);
            }

            return new Size(finalSize.Width, y + rowHeight);
        }
    }
}
