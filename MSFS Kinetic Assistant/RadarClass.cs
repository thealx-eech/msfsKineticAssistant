using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MSFS_Kinetic_Assistant
{
    class RadarClass
    {
        public void InitRadar(Canvas RadarCanvas, double coverScale, double allowedRadarScale)
        {
            Canvas group = new Canvas();
            group.Width = 250;
            group.Height = 250;
            group.Name = "CompassRose";

            for (int i = 0; i < 36; i++)
            {
                Polyline polyline1 = new Polyline();
                polyline1.Points.Add(new Point(125, 0));
                polyline1.Points.Add(new Point(125, i % 9 == 0 ? 20 : 10));
                polyline1.Stroke = i == 0 ? Brushes.DarkRed : Brushes.Black;
                polyline1.StrokeThickness = i % 9 == 0 ? 2 : 1;

                RotateTransform rotateTransform = new RotateTransform(10 * i, 125, 125);
                polyline1.RenderTransform = rotateTransform;

                Canvas.SetLeft(polyline1, 0);
                Canvas.SetTop(polyline1, 0);
                group.Children.Add(polyline1);

                if (i % 9 == 0)
                {
                    TextBlock northLabel = new TextBlock();
                    northLabel.FontSize = 14;
                    northLabel.Margin = new Thickness(100, 20, 100, 0);
                    northLabel.Width = 50;
                    northLabel.TextAlignment = TextAlignment.Center;

                    switch (i)
                    {
                        case 0:
                            northLabel.Text = "N";
                            northLabel.Foreground = new SolidColorBrush(Colors.DarkRed);
                            break;
                        case 9:
                            northLabel.Text = "E";
                            northLabel.Foreground = new SolidColorBrush(Colors.Black);
                            break;
                        case 18:
                            northLabel.Text = "S";
                            northLabel.Foreground = new SolidColorBrush(Colors.Black);
                            break;
                        case 27:
                            northLabel.Text = "W";
                            northLabel.Foreground = new SolidColorBrush(Colors.Black);
                            break;
                    }

                    RotateTransform rotateTransform2 = new RotateTransform(10 * i, 25, 105);
                    northLabel.RenderTransform = rotateTransform2;
                    group.Children.Add(northLabel);
                }
            }

            Canvas.SetZIndex(group, 1001);
            RadarCanvas.Children.Add(group);

            Canvas group2 = new Canvas();
            group2.Width = 250;
            group2.Height = 250;
            group2.Name = "WindPointer";

            Polygon polygon = new Polygon();
            polygon.Points.Add(new Point(125, 20));
            polygon.Points.Add(new Point(145, 0));
            polygon.Points.Add(new Point(105, 0));
            polygon.Fill = Brushes.DarkBlue;

            Canvas.SetLeft(polygon, 0);
            Canvas.SetTop(polygon, 0);
            group2.Children.Add(polygon);

            TextBlock windForce = new TextBlock();
            windForce.Foreground = new SolidColorBrush(Colors.White);
            windForce.FontSize = 14;
            windForce.Margin = new Thickness(100, -2, 100, 0);
            windForce.Width = 50;
            windForce.Text = "0";
            windForce.TextAlignment = TextAlignment.Center;
            group2.Children.Add(windForce);
            Canvas.SetZIndex(group2, 1002);

            RadarCanvas.Children.Add(group2);

            Canvas group3 = new Canvas();
            group3.Width = 50;
            group3.Height = 30;
            group3.Name = "Plane";
            group3.Opacity = 0.5;
            group3.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/media/gliderIcon.png")));

            Line dashV = new Line();
            Line dashH = new Line();
            dashV.Fill = dashH.Fill = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255));
            dashV.Stroke = dashH.Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0, 255));
            DoubleCollection dashArray = new DoubleCollection();
            dashArray.Add(2);
            dashArray.Add(4);
            dashH.StrokeDashArray = dashV.StrokeDashArray = dashArray;
            dashH.StrokeThickness = 1;
            dashH.X1 = dashV.Y1 = 0;
            dashH.X2 = dashV.Y2 = 250;
            dashH.Y1 = dashH.Y2 = dashV.X1 = dashV.X2 = 125;

            RadarCanvas.Children.Add(dashH);
            Canvas.SetZIndex(dashH, 1003);
            RadarCanvas.Children.Add(dashV);
            Canvas.SetZIndex(dashV, 1003);


            Canvas.SetLeft(group3, 100);
            Canvas.SetTop(group3, 110);
            Canvas.SetZIndex(group3, 1003);

            RadarCanvas.Children.Add(group3);

            if (allowedRadarScale < 50)
            {
                Canvas group4 = new Canvas();
                group4.Name = "RadarCover";
                group4.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/media/RadarCover.png")));
                Canvas.SetZIndex(group4, 1000);
                RadarCanvas.Children.Add(group4);
                updateRadarCover(RadarCanvas, coverScale);
            }
        }
        public void ClearRadar(Canvas RadarCanvas)
        {
            RadarCanvas.Children.Clear();
        }

        // COVER
        public void updateRadarCover(Canvas RadarCanvas, double coverScale)
        {
            foreach (var el in RadarCanvas.Children)
            {
                if (el.GetType() == typeof(Canvas) && ((Canvas)el).Name == "RadarCover")
                {
                    Canvas cover = ((Canvas)el);
                    cover.Width = 250 / coverScale;
                    cover.Height = 250 / coverScale;
                    Canvas.SetLeft(cover, -cover.Width / 2 + 125);
                    Canvas.SetTop(cover, -cover.Height / 2 + 125);

                    break;
                }
            }
        }

        // COMPASS
        public void updateCompassWind(Canvas RadarCanvas, double heading, double windDirection, double windForce)
        {
            foreach (var el in RadarCanvas.Children)
            {
                if (el.GetType() == typeof(Canvas) && ((Canvas)el).Name == "CompassRose")
                {
                    RotateTransform rotateTransform1 = new RotateTransform(-heading * 180 / Math.PI, 125, 125);
                    ((Canvas)el).RenderTransform = rotateTransform1;
                }
                else if (el.GetType() == typeof(Canvas) && ((Canvas)el).Name == "WindPointer")
                {
                    RotateTransform rotateTransform1 = new RotateTransform((-heading + windDirection) * 180 / Math.PI, 125, 125);
                    ((Canvas)el).RenderTransform = rotateTransform1;

                    foreach (var lbl in ((Canvas)el).Children)
                    {
                        if (lbl.GetType() == typeof(TextBlock))
                        {
                            ((TextBlock)lbl).Text = windForce.ToString("0");
                        }
                    }
                }
            }
        }

        // WINCH
        public void InsertWinch(Canvas RadarCanvas)
        {
            Canvas group = new Canvas();
            group.Width = 20;
            group.Height = 20;
            group.Name = "Winch";

            group.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/media/winchIcon.png")));
            group.Opacity = 0.5;

            RadarCanvas.Children.Add(group);
        }

        public void RemoveWinch(Canvas RadarCanvas)
        {
            try
            {
                var forRemoving = new List<Canvas>();
                foreach (UIElement el in RadarCanvas.Children)
                {
                    if (el.GetType() == typeof(Canvas) && (((Canvas)el).Name == "Winch"))
                    {
                        forRemoving.Add((Canvas)el);
                    }
                }

                foreach (var item in forRemoving)
                    RadarCanvas.Children.Remove(item);

            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED to clear winch: " + ex.Message);
            }
        }

        public void UpdateWinch(Canvas RadarCanvas, winchDirection winchPosition, double scale)
        {
            scale = Math.Max(0.1, scale) / 125 * 1000;

            try
            {
                foreach (UIElement el in RadarCanvas.Children)
                {
                    if (el.GetType() == typeof(Canvas) && (((Canvas)el).Name == "Winch"))
                    {
                        Canvas canvas = (Canvas)el;
                        Canvas.SetLeft(canvas, 125 - 10 + winchPosition.groundDistance / scale * Math.Sin(winchPosition.heading));
                        Canvas.SetTop(canvas, 125 - 10 - winchPosition.groundDistance / scale * Math.Cos(winchPosition.heading));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED to update winch position: " + ex.Message);
            }
        }

        // NEARBY
        public void InsertRadarNearby(Canvas RadarCanvas, SortedDictionary<Double, Button> nearbyDict, MainWindow parent)
        {
            clearRadarNearby(RadarCanvas);

            Canvas group = new Canvas();
            group.Width = 250;
            group.Height = 250;
            group.Name = "Nearbies";
            Canvas.SetZIndex(group, 1005);

            int id = 0;

            foreach (KeyValuePair<double, Button> btn in nearbyDict)
            {
                Ellipse ellipse = new Ellipse();
                ellipse.Name = "Nearby_" + btn.Value.Tag.ToString();
                ellipse.Visibility = Visibility.Collapsed;
                group.Children.Add(ellipse);

                Button label = new Button();
                label.Cursor = Cursors.Hand;
                label.Name = "NearbyLabel_" + btn.Value.Tag.ToString();
                label.Tag = btn.Value.Tag;
                label.Click += parent.attachTowCable;
                label.Content = btn.Value.Content.ToString().Split('(')[0];
                label.BorderThickness = new Thickness(0);
                label.Visibility = Visibility.Collapsed;
                label.Background = new SolidColorBrush(Colors.Transparent);
                group.Children.Add(label);

                id++;
            }

            Console.WriteLine(id + " radar nearbies loaded");
            RadarCanvas.Children.Add(group);
        }

        public void clearRadarNearby(Canvas RadarCanvas)
        {
            try
            {
                var forRemoving = new List<Canvas>();
                foreach (UIElement el in RadarCanvas.Children)
                {
                    if (el.GetType() == typeof(Canvas) && (((Canvas)el).Name == "Nearbies"))
                    {
                        forRemoving.Add((Canvas)el);
                    }
                }

                foreach (var item in forRemoving)
                    RadarCanvas.Children.Remove(item);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED to clear radar nearbies: " + ex.Message);
            }
        }

        public void updateRadarNearby(Canvas RadarCanvas, uint id, winchDirection planeDirection, winchPosition planePosition, bool currentTowPlane, double scale)
        {
            scale = Math.Max(0.1, scale) / 125 * 1000;

            try
            {
                foreach (UIElement canv in RadarCanvas.Children)
                {
                    if (canv.GetType() == typeof(Canvas) && ((Canvas)canv).Name == "Nearbies")
                    {
                        foreach (UIElement el in ((Canvas)canv).Children)
                        {
                            if (el.GetType() == typeof(Ellipse) && ((Ellipse)el).Name == "Nearby_" + id.ToString())
                            {
                                Ellipse circle = (Ellipse)el;

                                circle.Width = 10;
                                circle.Height = 10;
                                SolidColorBrush strokeBrush = new SolidColorBrush(currentTowPlane ? Colors.DarkRed : Colors.DarkGreen);
                                //strokeBrush.Opacity = Math.Abs(finalModifier);
                                circle.Fill = strokeBrush;
                                circle.Visibility = Visibility.Visible;
                                Canvas.SetLeft(circle, 125 - circle.Width / 2 + planeDirection.groundDistance / scale * Math.Sin(planeDirection.heading));
                                Canvas.SetTop(circle, 125 - circle.Height / 2 - planeDirection.groundDistance / scale * Math.Cos(planeDirection.heading));
                            }
                            else if (el.GetType() == typeof(Button) && ((Button)el).Name == "NearbyLabel_" + id.ToString())
                            {
                                Button label = (Button)el;

                                label.Visibility = Visibility.Visible;
                                label.Foreground = new SolidColorBrush(currentTowPlane ? Colors.DarkRed : Colors.DarkGreen);
                                label.Margin = new Thickness(125 + planeDirection.groundDistance / scale * Math.Sin(planeDirection.heading),
                                    125 - planeDirection.groundDistance / scale * Math.Cos(planeDirection.heading), 0, 0);
                            }
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to update radar nearby: " + ex.Message);
            }
        }


        // THERMALS
        public void insertRadarThermals(Canvas RadarCanvas, List<winchPosition> thermalsList, string lbl)
        {
            Canvas group = new Canvas();
            group.Width = 250;
            group.Height = 250;
            group.Name = lbl + "s";

            int id = 0;
            for (; id < thermalsList.Count; id++)
            {
                Ellipse ellipse = new Ellipse();
                ellipse.Name = lbl + "_" + id;
                group.Children.Add(ellipse);

                Label label = new Label();
                label.Name = "Label_" + lbl + "_" + id;
                label.FontSize = 10;
                label.Content = Math.Round(thermalsList[id].airspeed) + "m/s" + (thermalsList[id].alt > 1000 ? Environment.NewLine + (thermalsList[id].alt / 0.305).ToString("0") + "ft AGL" : "");
                label.Foreground = new SolidColorBrush(Colors.DarkRed);
                group.Children.Add(label);
            }

            Console.WriteLine(id + " radar " + lbl + "s loaded");
            RadarCanvas.Children.Add(group);
        }

        public void clearRadarThermals(Canvas RadarCanvas, string lbl)
        {
            try
            {
                var forRemoving = new List<Canvas>();
                foreach (UIElement el in RadarCanvas.Children)
                {
                    if (el.GetType() == typeof(Canvas) && (((Canvas)el).Name == lbl + "s"))
                    {
                        forRemoving.Add((Canvas)el);
                    }
                }

                foreach (var item in forRemoving)
                    RadarCanvas.Children.Remove(item);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED to clear radar thermals: " + ex.Message);
            }
        }

        public void updateRadarThermals(Canvas RadarCanvas, string id, winchDirection thermalDirection, winchPosition thermalPosition, double finalModifier, double scale)
        {
            scale = Math.Max(0.1, scale) / 125 * 1000;

            try
            {
                foreach (UIElement canv in RadarCanvas.Children)
                {
                    if (canv.GetType() == typeof(Canvas) && ((Canvas)canv).Name == id.Split('_')[0] + "s")
                    {
                        foreach (UIElement el in ((Canvas)canv).Children)
                        {
                            if (el.GetType() == typeof(Ellipse) && ((Ellipse)el).Name == id)
                            {
                                Ellipse circle = (Ellipse)el;

                                circle.Width = thermalPosition.radius * 2 / scale;
                                circle.Height = thermalPosition.radius * 2 / scale;
                                SolidColorBrush strokeBrush = new SolidColorBrush(finalModifier >= 0 ? Colors.Red : Colors.Blue);
                                strokeBrush.Opacity = Math.Abs(finalModifier >= 0 ? Math.Max(0.1, finalModifier) : Math.Min(-0.1, finalModifier));
                                circle.Fill = strokeBrush;
                                Canvas.SetLeft(circle, 125 - circle.Width / 2 + thermalDirection.groundDistance / scale * Math.Sin(thermalDirection.heading));
                                Canvas.SetTop(circle, 125 - circle.Height / 2 - thermalDirection.groundDistance / scale * Math.Cos(thermalDirection.heading));

                                //Console.WriteLine(id + " scale: " + scale + " modif: " + finalModifier);

                                //break;
                            }
                            else if (el.GetType() == typeof(Label) && ((Label)el).Name == "Label_" + id)
                            {
                                Label label = (Label)el;

                                Canvas.SetLeft(label, 110 + thermalDirection.groundDistance / scale * Math.Sin(thermalDirection.heading));
                                Canvas.SetTop(label, 115 - thermalDirection.groundDistance / scale * Math.Cos(thermalDirection.heading));

                                //break;
                            }
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to update radar thermals: " + ex.Message);
            }
        }

        // WAYPOINTS
        public string sanitizeString(string name)
        {
            if (name.Contains('|'))
                name = name.Split('|')[0];

            Regex rgx = new Regex("[^a-zA-Z0-9]");
            return rgx.Replace(name, "_");
        }
        public void insertRadarWaypoints(Canvas RadarCanvas, List<Waypoint> waypointsList)
        {
            clearRadarWaypoints(RadarCanvas);

            Canvas group = new Canvas();
            group.Width = 250;
            group.Height = 250;
            group.Name = "Waypoints";

            int id = 0;
            foreach (var wp in waypointsList)
            {
                /*Ellipse ellipse = new Ellipse();
                ellipse.Name = "Waypoint_" + wp.Name;*/
                Canvas arc = new Canvas();
                arc.Name = "Waypoint_" + sanitizeString(wp.Name) + wp.ID; // UNIQUE NAME!
                group.Children.Add(arc);

                Line leg = new Line();
                leg.Name = "Leg_" + sanitizeString(wp.Name) + wp.ID;
                group.Children.Add(leg);

                Label label = new Label();
                label.Name = "Label_" + sanitizeString(wp.Name) + wp.ID;
                label.FontSize = 10;
                label.Content = wp.Name + Environment.NewLine + "MIN: " + wp.Elevation + "ft" + Environment.NewLine + "MAX: " + wp.Height + "ft";
                group.Children.Add(label);

                id++;
            }

            Console.WriteLine(id + " radar waypoints loaded");
            RadarCanvas.Children.Add(group);
        }

        public void clearRadarWaypoints(Canvas RadarCanvas)
        {
            try
            {
                var forRemoving = new List<Canvas>();
                foreach (UIElement el in RadarCanvas.Children)
                {
                    if (el.GetType() == typeof(Canvas) && (((Canvas)el).Name == "Waypoints"))
                    {
                        forRemoving.Add((Canvas)el);
                    }
                }

                foreach (var item in forRemoving)
                    RadarCanvas.Children.Remove(item);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED to clear radar waypoints: " + ex.Message);
            }
        }

        public void updateRadarWaypoints(Canvas RadarCanvas, Waypoint wp, double distance, double heading, double width, double scale, double nextBearing, double nextDistance, double altitude)
        {
            scale = Math.Max(0.1, scale) / 125 * 1000;

            try
            {
                foreach (UIElement canv in RadarCanvas.Children)
                {
                    if (canv.GetType() == typeof(Canvas) && ((Canvas)canv).Name == "Waypoints")
                    {
                        foreach (var el in ((Canvas)canv).Children)
                        {
                            double opacity = !string.IsNullOrEmpty(wp.Entered) || !string.IsNullOrEmpty(wp.Passed) ? 0.1 : 0.5; ;

                            if (el.GetType() == typeof(Canvas) && ((Canvas)el).Name == "Waypoint_" + sanitizeString(wp.Name) + wp.ID)
                            {
                                Canvas cnv = (Canvas)el;

                                cnv.Width = width / scale;
                                cnv.Height = width / 2 / scale;
                                Canvas.SetLeft(cnv, 125 - cnv.Width / 2 + distance / scale * Math.Sin(heading));
                                Canvas.SetTop(cnv, 125 - distance / scale * Math.Cos(heading));
                                cnv.Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/media/wp.png")));
                                cnv.Background.Opacity = opacity;

                                RotateTransform rotateTransform = new RotateTransform(nextBearing * 180 / Math.PI, cnv.Width / 2, 0);
                                cnv.RenderTransform = rotateTransform;

                                //break;
                            }
                            else if (el.GetType() == typeof(Line) && ((Line)el).Name == "Leg_" + sanitizeString(wp.Name) + wp.ID)
                            {
                                Line leg = (Line)el;

                                SolidColorBrush strokeBrush = new SolidColorBrush(Color.FromArgb(255, 0, 170, 0));
                                strokeBrush.Opacity = 0.5;
                                leg.Stroke = strokeBrush;

                                leg.X1 = 125 + distance / scale * Math.Sin(heading);
                                leg.X2 = 125 + distance / scale * Math.Sin(heading) + nextDistance / scale * Math.Sin(nextBearing); ;
                                leg.Y1 = 125 - distance / scale * Math.Cos(heading);
                                leg.Y2 = 125 - distance / scale * Math.Cos(heading) - nextDistance / scale * Math.Cos(nextBearing);

                                //break;
                            }
                            else if (el.GetType() == typeof(Label) && ((Label)el).Name == "Label_" + sanitizeString(wp.Name) + wp.ID)
                            {
                                Label label = (Label)el;
                                if (altitude == 0)
                                    label.Foreground = new SolidColorBrush(Colors.Black);
                                else
                                    label.Foreground = new SolidColorBrush(altitude > 0 ? Colors.DarkRed : Colors.DarkBlue);

                                Canvas.SetLeft(label, 125 + distance / scale * Math.Sin(heading));
                                Canvas.SetTop(label, 125 - distance / scale * Math.Cos(heading));

                                break;
                            }
                        }

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to update radar waypoints: " + ex.Message);
            }
        }
    }
}
