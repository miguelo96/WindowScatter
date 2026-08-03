using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WindowScatter
{
    internal class WindowLayoutCalculator
    {
        private readonly Random rng;

        public WindowLayoutCalculator(Random rng)
        {
            this.rng = rng;
        }

        public List<WindowLayout> CalculateLayouts(List<WindowInfo> windows, double screenW, double screenH)
        {
            double padding = 50;
            double availableW = screenW - (2 * padding);
            double availableH = screenH - (2 * padding);

            double totalOriginalArea = 0;
            foreach (var win in windows)
            {
                var r = win.OriginalRect;
                totalOriginalArea += (r.Right - r.Left) * (r.Bottom - r.Top);
            }

            double targetTotalArea = availableW * availableH * 0.65;
            double scaleFactor = Math.Sqrt(targetTotalArea / totalOriginalArea);
            scaleFactor = Math.Max(0.15, Math.Min(0.5, scaleFactor));

            var layouts = CreateScaledLayouts(windows, scaleFactor);

            layouts = layouts.OrderByDescending(l => l.Width * l.Height).ToList();

            var placedWindows = new List<Rect>();

            if (layouts.Count == 2)
            {
                PositionTwoWindows(layouts, screenW, screenH, placedWindows);
            }
            else
            {
                PositionMultipleWindows(layouts, screenW, screenH, padding, availableW, availableH, placedWindows);
            }

            // Preserve the user's spatial context by assigning each window to the nearest slot.
            layouts = AssignWindowsToLayouts(windows, layouts);

            layouts.Reverse();
            return layouts;
        }

        private List<WindowLayout> CreateScaledLayouts(List<WindowInfo> windows, double scaleFactor)
        {
            var layouts = new List<WindowLayout>();

            foreach (var win in windows)
            {
                var r = win.OriginalRect;
                double origW = r.Right - r.Left;
                double origH = r.Bottom - r.Top;

                double w = origW * scaleFactor;
                double h = origH * scaleFactor;

                double sizeVariance = 0.85 + (rng.NextDouble() * 0.3);
                w *= sizeVariance;
                h *= sizeVariance;

                w = Math.Max(180, Math.Min(600, w));
                h = Math.Max(120, Math.Min(450, h));

                layouts.Add(new WindowLayout
                {
                    Window = win,
                    Width = w,
                    Height = h,
                    OrigWidth = origW,
                    OrigHeight = origH,
                    X = 0,
                    Y = 0
                });
            }

            return layouts;
        }

        private void PositionTwoWindows(List<WindowLayout> layouts, double screenW, double screenH, List<Rect> placedWindows)
        {
            double totalWidth = layouts[0].Width + layouts[1].Width + 40;
            double maxHeight = Math.Max(layouts[0].Height, layouts[1].Height);

            double startX = (screenW - totalWidth) / 2;
            double startY = (screenH - maxHeight) / 2;

            layouts[0].X = startX;
            layouts[0].Y = startY + (maxHeight - layouts[0].Height) / 2;
            placedWindows.Add(new Rect(layouts[0].X, layouts[0].Y, layouts[0].Width, layouts[0].Height));

            layouts[1].X = startX + layouts[0].Width + 40;
            layouts[1].Y = startY + (maxHeight - layouts[1].Height) / 2;
            placedWindows.Add(new Rect(layouts[1].X, layouts[1].Y, layouts[1].Width, layouts[1].Height));
        }

        private void PositionMultipleWindows(List<WindowLayout> layouts, double screenW, double screenH,
            double padding, double availableW, double availableH, List<Rect> placedWindows)
        {
            foreach (var layout in layouts)
            {
                Rect bestRect = new Rect(-9999, -9999, layout.Width, layout.Height);
                double bestScore = double.MaxValue;
                bool foundValidPosition = false;

                int maxResizeAttempts = 5;
                double originalWidth = layout.Width;
                double originalHeight = layout.Height;

                if (placedWindows.Count == 0)
                {
                    bestRect = PositionFirstWindow(layout, screenW, screenH, padding);
                    foundValidPosition = true;

                    layout.X = bestRect.X;
                    layout.Y = bestRect.Y;
                    placedWindows.Add(bestRect);
                    continue;
                }

                for (int resizeAttempt = 0; resizeAttempt < maxResizeAttempts && !foundValidPosition; resizeAttempt++)
                {
                    if (resizeAttempt > 0)
                    {
                        double shrinkFactor = 1.0 - (resizeAttempt * 0.1);
                        layout.Width = Math.Max(120, originalWidth * shrinkFactor);
                        layout.Height = Math.Max(80, originalHeight * shrinkFactor);
                    }

                    int gridSteps = 20;
                    double stepX = Math.Max(1, (availableW - layout.Width) / gridSteps);
                    double stepY = Math.Max(1, (availableH - layout.Height) / gridSteps);

                    for (int ix = 0; ix <= gridSteps; ix++)
                    {
                        for (int iy = 0; iy <= gridSteps; iy++)
                        {
                            double x = padding + (ix * stepX);
                            double y = padding + (iy * stepY);

                            if (x + layout.Width > screenW - padding || y + layout.Height > screenH - padding)
                                continue;

                            var candidateRect = new Rect(x, y, layout.Width, layout.Height);

                            if (HasOverlap(candidateRect, placedWindows))
                                continue;

                            double score = CalculatePositionScore(x, y, layout, screenW, screenH, placedWindows);

                            if (score < bestScore)
                            {
                                bestScore = score;
                                bestRect = candidateRect;
                                foundValidPosition = true;
                            }
                        }
                    }
                }

                if (!foundValidPosition)
                {
                    double centerX = (screenW - layout.Width) / 2;
                    double centerY = (screenH - layout.Height) / 2;
                    bestRect = new Rect(centerX, centerY, layout.Width, layout.Height);
                }

                layout.X = bestRect.X;
                layout.Y = bestRect.Y;
                placedWindows.Add(bestRect);
            }
        }

        private Rect PositionFirstWindow(WindowLayout layout, double screenW, double screenH, double padding)
        {
            double centerX = (screenW - layout.Width) / 2;
            double centerY = (screenH - layout.Height) / 2;

            double randomOffsetX = (rng.NextDouble() - 0.3) * 150;
            double randomOffsetY = (rng.NextDouble() - 0.5) * 100;

            centerX += randomOffsetX;
            centerY += randomOffsetY;

            centerX = Math.Max(padding, Math.Min(screenW - padding - layout.Width, centerX));
            centerY = Math.Max(padding, Math.Min(screenH - padding - layout.Height, centerY));

            return new Rect(centerX, centerY, layout.Width, layout.Height);
        }

        private bool HasOverlap(Rect candidateRect, List<Rect> placedWindows)
        {
            foreach (var placed in placedWindows)
            {
                var expandedPlaced = new Rect(
                    placed.X - 15,
                    placed.Y - 15,
                    placed.Width + 30,
                    placed.Height + 30
                );

                var intersection = Rect.Intersect(candidateRect, expandedPlaced);

                if (!intersection.IsEmpty && intersection.Width > 0.1 && intersection.Height > 0.1)
                    return true;
            }

            return false;
        }

        private double CalculatePositionScore(double x, double y, WindowLayout layout,
            double screenW, double screenH, List<Rect> placedWindows)
        {
            double score = 0;
            double screenCenterX = screenW / 2;
            double screenCenterY = screenH / 2;
            double currentX = x + layout.Width / 2;
            double currentY = y + layout.Height / 2;

            int leftCount = placedWindows.Count(p => (p.X + p.Width / 2) < screenCenterX);
            int rightCount = placedWindows.Count(p => (p.X + p.Width / 2) >= screenCenterX);
            int topCount = placedWindows.Count(p => (p.Y + p.Height / 2) < screenCenterY);
            int bottomCount = placedWindows.Count(p => (p.Y + p.Height / 2) >= screenCenterY);

            if (leftCount > rightCount && currentX >= screenCenterX)
                score -= 600 * (leftCount - rightCount);
            else if (rightCount > leftCount && currentX < screenCenterX)
                score -= 600 * (rightCount - leftCount);

            if (topCount > bottomCount && currentY >= screenCenterY)
                score -= 400 * (topCount - bottomCount);
            else if (bottomCount > topCount && currentY < screenCenterY)
                score -= 400 * (bottomCount - topCount);

            score += CalculateQuadrantScore(currentX, currentY, screenCenterX, screenCenterY, placedWindows);

            double padding = 50;
            double distToTopLeft = Math.Sqrt(Math.Pow(x - padding, 2) + Math.Pow(y - padding, 2));
            double distToTopRight = Math.Sqrt(Math.Pow(x + layout.Width - (screenW - padding), 2) + Math.Pow(y - padding, 2));
            double distToBottomLeft = Math.Sqrt(Math.Pow(x - padding, 2) + Math.Pow(y + layout.Height - (screenH - padding), 2));
            double distToBottomRight = Math.Sqrt(Math.Pow(x + layout.Width - (screenW - padding), 2) + Math.Pow(y + layout.Height - (screenH - padding), 2));
            double nearestCorner = Math.Min(Math.Min(distToTopLeft, distToTopRight), Math.Min(distToBottomLeft, distToBottomRight));
            score += nearestCorner * 0.5;

            double distToLeftEdge = Math.Abs(x - padding);
            double distToRightEdge = Math.Abs(x + layout.Width - (screenW - padding));
            double distToTopEdge = Math.Abs(y - padding);
            double distToBottomEdge = Math.Abs(y + layout.Height - (screenH - padding));
            double nearestEdge = Math.Min(Math.Min(distToLeftEdge, distToRightEdge), Math.Min(distToTopEdge, distToBottomEdge));
            score += nearestEdge * 0.05;

            double distFromCenter = Math.Sqrt(Math.Pow(currentX - screenCenterX, 2) + Math.Pow(currentY - screenCenterY, 2));
            score += distFromCenter * 0.20;

            double minDistToPlaced = double.MaxValue;
            foreach (var placed in placedWindows)
            {
                double dx = Math.Max(0, Math.Max(placed.X - (x + layout.Width), x - (placed.X + placed.Width)));
                double dy = Math.Max(0, Math.Max(placed.Y - (y + layout.Height), y - (placed.Y + placed.Height)));
                double dist = Math.Sqrt(dx * dx + dy * dy);
                minDistToPlaced = Math.Min(minDistToPlaced, dist);
            }
            score += minDistToPlaced * 0.05;

            score += rng.NextDouble() * 3;

            return score;
        }

        private double CalculateQuadrantScore(double currentX, double currentY, double screenCenterX,
            double screenCenterY, List<Rect> placedWindows)
        {
            int topLeftQuad = placedWindows.Count(p => p.X + p.Width / 2 < screenCenterX && p.Y + p.Height / 2 < screenCenterY);
            int topRightQuad = placedWindows.Count(p => p.X + p.Width / 2 >= screenCenterX && p.Y + p.Height / 2 < screenCenterY);
            int bottomLeftQuad = placedWindows.Count(p => p.X + p.Width / 2 < screenCenterX && p.Y + p.Height / 2 >= screenCenterY);
            int bottomRightQuad = placedWindows.Count(p => p.X + p.Width / 2 >= screenCenterX && p.Y + p.Height / 2 >= screenCenterY);

            int minQuadCount = Math.Min(Math.Min(topLeftQuad, topRightQuad), Math.Min(bottomLeftQuad, bottomRightQuad));

            bool isTopLeft = currentX < screenCenterX && currentY < screenCenterY;
            bool isTopRight = currentX >= screenCenterX && currentY < screenCenterY;
            bool isBottomLeft = currentX < screenCenterX && currentY >= screenCenterY;
            bool isBottomRight = currentX >= screenCenterX && currentY >= screenCenterY;

            if ((isTopLeft && topLeftQuad == minQuadCount) ||
                (isTopRight && topRightQuad == minQuadCount) ||
                (isBottomLeft && bottomLeftQuad == minQuadCount) ||
                (isBottomRight && bottomRightQuad == minQuadCount))
            {
                return -1000;
            }

            return 0;
        }

        private List<WindowLayout> AssignWindowsToLayouts(List<WindowInfo> windows, List<WindowLayout> layouts)
        {
            var windowToLayoutMap = new Dictionary<WindowInfo, WindowLayout>();
            var availableLayouts = new List<WindowLayout>(layouts);

            foreach (var win in windows)
            {
                double winCenterX = (win.OriginalRect.Left + win.OriginalRect.Right) / 2.0;
                double winCenterY = (win.OriginalRect.Top + win.OriginalRect.Bottom) / 2.0;

                WindowLayout? closestLayout = null;
                double minDistance = double.MaxValue;

                foreach (var layout in availableLayouts)
                {
                    double layoutCenterX = layout.X + (layout.Width / 2.0);
                    double layoutCenterY = layout.Y + (layout.Height / 2.0);

                    double dx = layoutCenterX - winCenterX;
                    double dy = layoutCenterY - winCenterY;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestLayout = layout;
                    }
                }

                if (closestLayout != null)
                {
                    windowToLayoutMap[win] = closestLayout;
                    availableLayouts.Remove(closestLayout);
                }
            }

            var finalLayouts = new List<WindowLayout>();

            foreach (var win in windows)
            {
                if (!windowToLayoutMap.ContainsKey(win)) continue;

                var assignedLayout = windowToLayoutMap[win];
                var r = win.OriginalRect;
                double origW = r.Right - r.Left;
                double origH = r.Bottom - r.Top;
                double windowAspectRatio = origW / origH;
                double layoutAspectRatio = assignedLayout.Width / assignedLayout.Height;

                double newWidth, newHeight;

                if (windowAspectRatio > layoutAspectRatio)
                {
                    newWidth = assignedLayout.Width;
                    newHeight = newWidth / windowAspectRatio;
                }
                else
                {
                    newHeight = assignedLayout.Height;
                    newWidth = newHeight * windowAspectRatio;
                }

                finalLayouts.Add(new WindowLayout
                {
                    Window = win,
                    X = assignedLayout.X,
                    Y = assignedLayout.Y,
                    Width = newWidth,
                    Height = newHeight,
                    OrigWidth = origW,
                    OrigHeight = origH
                });
            }

            return finalLayouts;
        }
    }
}
