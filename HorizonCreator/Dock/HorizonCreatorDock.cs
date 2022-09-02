#region "copyright"

/*
    Copyright © 2022 Christian Palm (christian@palm-family.de)
    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using HorizonCreator.Properties;
using NINA.Astrometry;
using NINA.Astrometry.Body;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.ViewModel;
using OpenCvSharp;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HorizonCreator.Dock
{
    [Export(typeof(IDockableVM))]
    public class HorizonCreatorDock : DockableVM
    {
        private IProfileService pService;
        private ITelescopeMediator telescope;
        private ICameraMediator camera;
        private IApplicationStatusMediator statusMediator;
        private IImagingMediator imaging;
        private CancellationTokenSource Token;
        private ApplicationStatus _status;
        private bool automatedHorizonRunning = false;
        private bool paused = false;
        private int progress;

        private List<Coordinate> points;
        private string x;
        public string Points
        {
            get
            {
                string res = string.Empty;
                foreach (var item in points)
                {
                    res += $"{item.Azimuth}      {item.Altitude}\n";
                }
                return res;
            }
            set 
            {
                x = value;
                RaisePropertyChanged();
            }
        }

        private double _stepSize = 5;
        public double StepSize
        {
            get { return _stepSize; }
            set
            {
                _stepSize = value;
                RaisePropertyChanged();
            }
        }

        public ApplicationStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;
                _status.Source = "Horizon Creator";
                RaisePropertyChanged(nameof(Status));

                statusMediator.StatusUpdate(_status);
            }
        }

        public PlateSolvingStatusVM PlateSolvingStatus { get; } = new PlateSolvingStatusVM();
        public RelayCommand AddPoint { get; set; }
        public RelayCommand Save { get; set; }
        public RelayCommand Clear { get; set; }
        public RelayCommand Minus { get; set; }
        public RelayCommand AutomatedHorizon { get; set; }
        public RelayCommand StopAutomatedHorizon { get; set; }
        public RelayCommand PauseAutomatedHorizon { get; set; }
        public RelayCommand ContinueAutomatedHorizon { get; set; }
        public RelayCommand Test { get; set; }
        public RelayCommand AltUp { get; set; }
        public RelayCommand AltDown { get; set; }
        public RelayCommand AzRight { get; set; }
        public RelayCommand AzLeft { get; set; }

        [ImportingConstructor]
        public HorizonCreatorDock(IProfileService profileService,
                                  ITelescopeMediator telescope,
                                  ICameraMediator camera,
                                  IApplicationStatusMediator statusMediator,
                                  IImagingMediator imaging,
                                  IImageDataFactory dataFactory,
                                  IPlateSolverFactory plateSolver,
                                  IFilterWheelMediator filterWheel,
                                  IDomeMediator domeMediator,
                                  IDomeFollower domeFollower) : base(profileService)
        {
            Title = "Horizon Creator";
            this.pService = profileService;
            this.telescope = telescope;
            this.camera = camera;
            this.statusMediator = statusMediator;
            this.imaging = imaging;
            points = new List<Coordinate>();
            Token = new CancellationTokenSource();


            AddPoint = new RelayCommand(_ =>
            {
                if (telescope.GetInfo().Connected)
                {
                    double altitude = telescope.GetInfo().Altitude;
                    double azimuth = telescope.GetInfo().Azimuth;

                    List<Coordinate> temp = points;
                    temp.Add(new Coordinate() { Altitude = altitude, Azimuth = azimuth });
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                } 
                else
                {
                    Notification.ShowWarning("Telescope is not connected!");
                }
            });

            Save = new RelayCommand(_ =>
            {
                Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save Horizon",
                    FileName = "horizon",
                    DefaultExt = ".hrz",
                    Filter = "Horizon File|*.hrz"
                };

                if (dialog.ShowDialog() == true)
                {                    
                    File.WriteAllText(dialog.FileName, Points);
                    Notification.ShowSuccess("Horizon saved at " + dialog.FileName);
                }
            });

            Clear = new RelayCommand(_ =>
            {
                points = new List<Coordinate>();
                RaisePropertyChanged(nameof(Points));
            });

            Minus = new RelayCommand(_ =>
            {
                if (points.Count > 0)
                {
                    List<Coordinate> temp = points;
                    temp.RemoveAt(temp.Count - 1);
                    points = temp;
                    RaisePropertyChanged(nameof(Points));
                }
            });

            AutomatedHorizon = new RelayCommand(async _ =>
            {
                /*
                 * Idea for future update:
                 * when a horizon is detected only move azimuth but stay at the same altitude
                 * if no horizon is detected slew to a lower altitude
                 * if horizon is detected slew to a higher altitude and check if it is still detected
                 * if not add coordinates to List
                 * 
                 * BUT
                 * unsafer because for example balconies may not be detected
                 */
                Sun sun = new Sun(DateTime.Now, profileService.ActiveProfile.AstrometrySettings.Latitude, profileService.ActiveProfile.AstrometrySettings.Longitude);

                if (sun.Altitude > 0)
                {
                    Notification.ShowError("The horizon can only be created when the sun is below the horizon!");
                    return;
                }

                if (!camera.GetInfo().Connected)
                {
                    Notification.ShowError("Camera is not connected!");
                    return;
                }

                if (telescope.GetInfo().Connected && telescope.GetInfo().CanMoveSecondaryAxis && telescope.GetInfo().CanMovePrimaryAxis)
                {
                    Token = new CancellationTokenSource();
                    if (!paused)
                        progress = 0;
                    int horizontalResolution = Settings.Default.HorizontalResolution;
                    int verticalResolution = Settings.Default.VerticalResolution;
                    double startAlt = Settings.Default.StartAltitude;
                    Angle latitude = Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Latitude);
                    Angle longitude = Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Longitude);
                    bool IsSouth = Settings.Default.IsSouth;
                    string fileName = "";
                    automatedHorizonRunning = true;

                    var plate = plateSolver.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings); // It is important to have a consistent plate solver!
                    var blind = plateSolver.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);

                    var capture = plateSolver.GetCaptureSolver(plate, blind, imaging, filterWheel);

                    var parameter = new CaptureSolverParameter() // Make sure all profile settings are correct!
                    {
                        Coordinates = telescope.GetCurrentPosition(),
                        DisableNotifications = true,
                        FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                        DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                        MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                        PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                        SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                        BlindFailoverEnabled = true,
                        ReattemptDelay = new TimeSpan(0, 0, 1)
                    };

                    Microsoft.Win32.SaveFileDialog dialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save Horizon",
                        FileName = "horizon",
                        DefaultExt = ".hrz",
                        Filter = "Horizon File|*.hrz"
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        fileName = dialog.FileName;
                    } else
                    {
                        Notification.ShowError("A Filepath has to be specified!");
                        return;
                    }

                    if (!paused)
                        await telescope.SlewToCoordinatesAsync(new Coordinates(IsSouth ? 90 : -90, 0, Epoch.JNOW, Coordinates.RAType.Degrees), Token.Token);
                    else
                        paused = false;

                    while (progress < 360)
                    {
                        bool foundHorizon = false;
                        do
                        {
                            TopocentricCoordinates topo = new TopocentricCoordinates(Angle.ByDegree(progress), Angle.ByDegree(telescope.GetInfo().Altitude - verticalResolution), latitude, longitude);
                            if (!await telescope.SlewToCoordinatesAsync(topo, Token.Token))
                            {
                                Notification.ShowError("Slew failed! Check the logs for more information.");
                                return;
                            }

                            if (Token.IsCancellationRequested)
                            {
                                break;
                            }

                            PlateSolveResult res = await capture.Solve(new CaptureSequence()
                            {
                                ExposureTime = 5,
                                TotalExposureCount = 1,
                                ImageType = "SNAPSHOT"
                            }, parameter, PlateSolvingStatus.Progress, new Progress<ApplicationStatus>(p => Status = p), Token.Token);

                            if (!res.Success)
                            {
                                Notification.ShowSuccess("Horizon!");
                                List<Coordinate> temp = points;
                                temp.Add(new Coordinate() { Altitude = telescope.GetInfo().Altitude, Azimuth = telescope.GetInfo().Azimuth });
                                points = temp;
                                foundHorizon = true;
                                RaisePropertyChanged(nameof(Points));
                            }
                            else
                            {
                                Notification.ShowSuccess("Sky!");
                            }

                        } while (!foundHorizon);

                        if (Token.IsCancellationRequested)
                        {
                            break;
                        }

                        TopocentricCoordinates coordinates = new TopocentricCoordinates(Angle.ByDegree(progress + horizontalResolution), Angle.ByDegree(startAlt), latitude, longitude);

                        await telescope.SlewToCoordinatesAsync(coordinates, Token.Token);
                    }

                    File.WriteAllText(fileName, Points);
                }
                else
                {
                    Notification.ShowError("Telescope is not connected or not compatible!");
                }
                automatedHorizonRunning = false;
            });

            StopAutomatedHorizon = new RelayCommand(_ =>
            {
                Token.Cancel();
                Token = new CancellationTokenSource();
                automatedHorizonRunning = false;
            });

            PauseAutomatedHorizon = new RelayCommand(_ =>
            {
                Token.Cancel();
                automatedHorizonRunning = false;
                paused = true;
            });

            ContinueAutomatedHorizon = new RelayCommand(_ =>
            {
                automatedHorizonRunning = true;
                AutomatedHorizon.Execute(null);
            });

            Test = new RelayCommand(async _ =>
            {
                #region Hough / edge idea
                // IImageData data = await dataFactory.CreateFromFile(@"C:\Users\Christian\Desktop\IMG_0621.tif", 8, false, RawConverterEnum.DCRAW);
                // IRenderedImage rendered = data.RenderImage();
                // rendered = await rendered.Stretch(3, -.7, true);
                // Mat edges = new Mat();
                // Cv2.CvtColor(BitmapConverter.ToMat(ImageUtility.Convert16BppTo8Bpp(rendered.Image)), edges, ColorConversionCodes.BayerRG2GRAY);
                // Cv2.Threshold(edges, edges, 0, 255, ThresholdTypes.Binary);
                // // Bitmap edges = new CannyEdgeDetector().Apply(ImageUtility.Convert16BppTo8Bpp(rendered.Image));
                // imaging.SetImage(rendered.Image);
                // await Task.Delay(1000);
                // imaging.SetImage(ImageUtility.ConvertBitmap(Get24bppRgb(BitmapConverter.ToBitmap(edges))));
                // 
                // // Apply hough transform to edges and show
                // Mat pic = CreateMat(ImageUtility.BitmapFromSource(rendered.Image));
                // LineSegmentPoint[] lines = Cv2.HoughLinesP(edges, 3, Math.PI / 180, 150);
                // foreach (LineSegmentPoint line in lines)
                // {
                //     Cv2.Line(pic, line.P1, line.P2, new Scalar(0, 0, 255));
                // }
                // Cv2.Resize(pic, pic, new OpenCvSharp.Size(1000, 1000));
                // Cv2.ImShow("Hough", pic);
                // Cv2.WaitKey(0);
                // Cv2.DestroyAllWindows();
                #endregion

                #region Platesolving issues
                var plate = plateSolver.GetPlateSolver(profileService.ActiveProfile.PlateSolveSettings); // It is important to have a consistent plate solver!
                var blind = plateSolver.GetBlindSolver(profileService.ActiveProfile.PlateSolveSettings);

                var capture = plateSolver.GetCaptureSolver(plate, blind, imaging, filterWheel);

                var parameter = new CaptureSolverParameter() // Make sure all profile settings are correct!
                {
                    Coordinates = telescope.GetCurrentPosition(),
                    DisableNotifications = true,
                    FocalLength = profileService.ActiveProfile.TelescopeSettings.FocalLength,
                    DownSampleFactor = profileService.ActiveProfile.PlateSolveSettings.DownSampleFactor,
                    MaxObjects = profileService.ActiveProfile.PlateSolveSettings.MaxObjects,
                    PixelSize = profileService.ActiveProfile.CameraSettings.PixelSize,
                    SearchRadius = profileService.ActiveProfile.PlateSolveSettings.SearchRadius,
                    BlindFailoverEnabled = true,
                    ReattemptDelay = new TimeSpan(0, 0, 1)
                };

                PlateSolveResult res = await capture.Solve(new CaptureSequence()
                {
                    ExposureTime = 5,
                    TotalExposureCount = 1,
                    ImageType = "SNAPSHOT"
                }, parameter, PlateSolvingStatus.Progress, new Progress<ApplicationStatus>(p => Status = p), Token.Token);

                if (!res.Success)
                {
                    Notification.ShowSuccess("Horizon!");
                }
                else
                {
                    Notification.ShowSuccess("Sky!");
                }
                // continue further, alt - y deg
                #endregion
            });
            
            AzRight = new RelayCommand(async _ =>
            {
                await SlewAz(StepSize);
            });

            AzLeft = new RelayCommand(async _ =>
            {
                await SlewAz(-StepSize);
            });

            AltUp = new RelayCommand(async _ =>
            {
                await SlewAlt(StepSize);
            });

            AltDown = new RelayCommand(async _ =>
            {
                await SlewAlt(-StepSize);
            });
        }

        private async Task SlewAlt(double stepSize)
        {
            if (automatedHorizonRunning)
            {
                Notification.ShowError("Automated Horizon is running!");
                return;
            }
            
            if (telescope.GetInfo().Connected)
            {
                double alt = telescope.GetInfo().Altitude;
                double az = telescope.GetInfo().Azimuth;
                TopocentricCoordinates coord = new TopocentricCoordinates(Angle.ByDegree(az), Angle.ByDegree(alt + stepSize), Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Longitude));
                await telescope.SlewToCoordinatesAsync(coord, Token.Token);
            }
            else
            {
                Notification.ShowWarning("Telescope is not connected!");
            }
        }

        private async Task SlewAz(double stepSize)
        {
            if (automatedHorizonRunning)
            {
                Notification.ShowError("Automated Horizon is running!");
                return;
            }
            
            if (telescope.GetInfo().Connected)
            {
                double alt = telescope.GetInfo().Altitude;
                double az = telescope.GetInfo().Azimuth;
                TopocentricCoordinates coord = new TopocentricCoordinates(Angle.ByDegree(az + stepSize), Angle.ByDegree(alt), Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Latitude), Angle.ByDegree(pService.ActiveProfile.AstrometrySettings.Longitude));
                await telescope.SlewToCoordinatesAsync(coord, Token.Token);
            }
            else
            {
                Notification.ShowWarning("Telescope is not connected!");
            }
        }

        private static Bitmap Get24bppRgb(Image image)
        {
            var bitmap = new Bitmap(image);
            var bitmap24 = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format24bppRgb);
            using (var gr = Graphics.FromImage(bitmap24))
            {
                gr.DrawImage(bitmap, new Rectangle(0, 0, bitmap24.Width, bitmap24.Height));
            }
            return bitmap24;
        }

        private Mat CreateMat(Bitmap bmp)
        {
            bmp = ImageUtility.Convert16BppTo8Bpp(ImageUtility.ConvertBitmap(bmp));
            Mat mat = new Mat(bmp.Height, bmp.Width, MatType.CV_8UC3);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            byte[] bytes = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            Marshal.Copy(bytes, 0, mat.Data, bytes.Length);
            bmp.UnlockBits(data);
            return mat;
        }

        private List<HistogramIndex> CreateHist(ImmutableList<DataPoint> points)
        {
            List<HistogramIndex> hist = new List<HistogramIndex>();
            foreach (DataPoint point in points)
            {
                foreach (HistogramIndex index in hist)
                {
                    if (index.Index == point.X)
                    {
                        index.Count++;
                        break;
                    }
                    hist.Add(new HistogramIndex() { Index = point.X, Count = 1 });
                }
            }

            return hist;
        }
    }

    public class Coordinate
    {
        public double Altitude { get; set; }
        public double Azimuth { get; set; }

        public override string ToString()
        {
            return $"{Azimuth} {Altitude}";
        }
    }

    public class HistogramIndex
    {
        public double Index { get; set; }
        public int Count { get; set; }
    }

    public class HorizonStatus
    {
        public int Progress { get; set; }
    }
}
